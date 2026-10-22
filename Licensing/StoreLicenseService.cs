// VBWR B
// Project: VoiceDucker | Creator: Umberto Giacobbi
// Modified with AI: OpenAI Codex; Microsoft Store licensing, 2026-10-12.
// SPDX-License-Identifier: MIT
// VBWR E

using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Windows.ApplicationModel;
using Windows.Services.Store;

namespace VoiceDucker.Licensing;

/// <summary>Reads Store-managed licensing and purchases while keeping local distributions unrestricted.</summary>
internal sealed class StoreLicenseService : IDisposable
{
    #region Private fields

    private const string StoreFamily = "UmbertoGiacobbiDotBiz.VoiceDucker_aa9ddh7dsmn36";

    private readonly DispatcherQueue _dispatcher;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    private StoreContext? _context;
    private bool _disposed;

    #endregion

    #region Initialization

    /// <summary>Detects the Store edition and connects its license notifications to the owning window.</summary>
    internal StoreLicenseService(Window window)
    {
        _dispatcher = window.DispatcherQueue;

        try
        {
            IsStoreEdition = Package.Current.Id.FamilyName == StoreFamily;
        }
        catch (InvalidOperationException)
        {
            // Portable builds have no package identity.
            IsStoreEdition = false;
        }

        State = IsStoreEdition ? LicenseState.Checking : LicenseState.Unrestricted;

        if (!IsStoreEdition)
        {
            return;
        }

        try
        {
            _context = StoreContext.GetDefault();

            WinRT.Interop.InitializeWithWindow.Initialize(
                _context,
                WinRT.Interop.WindowNative.GetWindowHandle(window));

            _context.OfflineLicensesChanged += OnOfflineLicensesChanged;
        }
        catch (Exception)
        {
            State = LicenseState.Unavailable;
        }
    }

    #endregion

    #region Public methods

    internal const string StoreId = "9P2F2FCRHS85";

    /// <summary>Gets the most recent entitlement classification.</summary>
    internal LicenseState State { get; private set; }

    /// <summary>Gets the expiration supplied by Microsoft Store.</summary>
    internal DateTimeOffset Expires { get; private set; }

    /// <summary>Identifies a trial entitlement, including a trial that has expired.</summary>
    internal bool IsTrialLicense { get; private set; }

    /// <summary>Gets the Store-formatted purchase price when it is available.</summary>
    internal string? FormattedPrice { get; private set; }

    /// <summary>Identifies packages governed by this application's Store entitlement.</summary>
    internal bool IsStoreEdition { get; }

    /// <summary>Applies the access policy to the latest entitlement and the current time.</summary>
    internal bool CanUseApp => LicensePolicy.CanUse(State, Expires, DateTimeOffset.UtcNow);

    /// <summary>Notifies the model after a license or displayed price changes.</summary>
    internal event Action? Changed;

    /// <summary>Refreshes the Windows-managed license and publishes its resulting access state.</summary>
    internal async Task RefreshAsync()
    {
        if (!IsStoreEdition || _disposed)
        {
            return;
        }

        await _refreshLock.WaitAsync();

        try
        {
            if (_disposed)
            {
                return;
            }

            if (_context is null)
            {
                State = LicenseState.Unavailable;
                return;
            }

            // Windows supplies the cached Store license offline. No custom trial clock or license file.
            var license = await _context.GetAppLicenseAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(15));

            Expires = license.ExpirationDate;
            IsTrialLicense = license.IsTrial;
            State = LicensePolicy.Evaluate(license.IsActive, license.IsTrial, Expires, DateTimeOffset.UtcNow);
        }
        catch (Exception)
        {
            State = LicenseState.Unavailable;
        }
        finally
        {
            _refreshLock.Release();

            if (!_disposed)
            {
                Changed?.Invoke();
            }
        }
    }

    /// <summary>Loads optional purchase copy without changing access when the price is unavailable.</summary>
    internal async Task LoadPriceAsync()
    {
        if (_context is null || _disposed)
        {
            return;
        }

        try
        {
            var result = await _context.GetStoreProductForCurrentAppAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(15));

            if (result.ExtendedError is null && result.Product is not null)
            {
                FormattedPrice = result.Product.Price.FormattedPrice;
            }

            if (!_disposed)
            {
                Changed?.Invoke();
            }
        }
        catch (Exception)
        {
            // A missing price must not invalidate a valid license.
        }
    }

    /// <summary>Requests the Store purchase and reports the refreshed entitlement or purchase outcome.</summary>
    internal async Task<string?> PurchaseAsync()
    {
        if (_context is null || _disposed)
        {
            return "Open Microsoft Store to purchase VoiceDucker, then retry the license check.";
        }

        try
        {
            var result = await _context.RequestPurchaseAsync(StoreId);

            await RefreshAsync();

            return result.Status switch
            {
                StorePurchaseStatus.Succeeded or StorePurchaseStatus.AlreadyPurchased =>
                    CanUseApp ? "Thank you. Your license is active." : "Purchase received. Retry the license check to activate it.",
                StorePurchaseStatus.NotPurchased => "Purchase cancelled. No license change was made.",
                StorePurchaseStatus.NetworkError => "Microsoft Store could not connect. Please try again when online.",
                _ => "Microsoft Store could not complete the purchase. Please try again."
            };
        }
        catch (Exception)
        {
            return "Microsoft Store is unavailable. Please try again.";
        }
    }

    /// <summary>Publishes expiration when an active trial reaches its Store-supplied end time.</summary>
    internal void CheckExpiration()
    {
        if (State != LicenseState.Trial || CanUseApp)
        {
            return;
        }

        State = LicenseState.Expired;
        Changed?.Invoke();
    }

    /// <summary>Stops observing Store changes and prevents subsequent refresh notifications.</summary>
    public void Dispose()
    {
        _disposed = true;

        if (_context is not null)
        {
            _context.OfflineLicensesChanged -= OnOfflineLicensesChanged;
        }
    }

    #endregion

    #region Implementation

    /// <summary>Marshals a Store license-change notification back to the owning dispatcher.</summary>
    private void OnOfflineLicensesChanged(StoreContext sender, object args)
    {
        _dispatcher.TryEnqueue(() =>
        {
            if (!_disposed)
            {
                _ = RefreshAsync();
            }
        });
    }

    #endregion
}

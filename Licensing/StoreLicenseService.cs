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

internal sealed class StoreLicenseService : IDisposable
{
    internal const string StoreId = "9P2F2FCRHS85";
    private const string StoreFamily = "UmbertoGiacobbiDotBiz.VoiceDucker_aa9ddh7dsmn36";
    private readonly DispatcherQueue _dispatcher;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private StoreContext? _context;
    private bool _disposed;

    internal LicenseState State { get; private set; }
    internal DateTimeOffset Expires { get; private set; }
    internal bool IsTrialLicense { get; private set; }
    internal string? FormattedPrice { get; private set; }
    internal bool IsStoreEdition { get; }
    internal bool CanUseApp => LicensePolicy.CanUse(State, Expires, DateTimeOffset.UtcNow);
    internal event Action? Changed;

    internal StoreLicenseService(Window window)
    {
        _dispatcher = window.DispatcherQueue;
        try { IsStoreEdition = Package.Current.Id.FamilyName == StoreFamily; }
        catch (InvalidOperationException) { IsStoreEdition = false; } // Portable build has no package identity.
        State = IsStoreEdition ? LicenseState.Checking : LicenseState.Unrestricted;
        if (!IsStoreEdition) return;
        try
        {
            _context = StoreContext.GetDefault();
            WinRT.Interop.InitializeWithWindow.Initialize(_context, WinRT.Interop.WindowNative.GetWindowHandle(window));
            _context.OfflineLicensesChanged += OnOfflineLicensesChanged;
        }
        catch (Exception) { State = LicenseState.Unavailable; }
    }

    internal async Task RefreshAsync()
    {
        if (!IsStoreEdition || _disposed) return;
        await _refreshLock.WaitAsync();
        try
        {
            if (_disposed) return;
            if (_context is null) { State = LicenseState.Unavailable; return; }
            // Windows supplies the cached Store license offline. No custom trial clock or license file.
            var license = await _context.GetAppLicenseAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(15));
            Expires = license.ExpirationDate;
            IsTrialLicense = license.IsTrial;
            State = LicensePolicy.Evaluate(license.IsActive, license.IsTrial, Expires, DateTimeOffset.UtcNow);
        }
        catch (Exception) { State = LicenseState.Unavailable; }
        finally
        {
            _refreshLock.Release();
            if (!_disposed) Changed?.Invoke();
        }
    }

    internal async Task LoadPriceAsync()
    {
        if (_context is null || _disposed) return;
        try
        {
            var result = await _context.GetStoreProductForCurrentAppAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(15));
            if (result.ExtendedError is null && result.Product is not null)
                FormattedPrice = result.Product.Price.FormattedPrice;
            if (!_disposed) Changed?.Invoke();
        }
        catch (Exception) { /* A missing price must not invalidate a valid license. */ }
    }

    internal async Task<string?> PurchaseAsync()
    {
        if (_context is null || _disposed) return "Open Microsoft Store to purchase VoiceDucker, then retry the license check.";
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
        catch (Exception) { return "Microsoft Store is unavailable. Please try again."; }
    }

    internal void CheckExpiration()
    {
        if (State != LicenseState.Trial || CanUseApp) return;
        State = LicenseState.Expired;
        Changed?.Invoke();
    }

    private void OnOfflineLicensesChanged(StoreContext sender, object args) =>
        _dispatcher.TryEnqueue(() => { if (!_disposed) _ = RefreshAsync(); });

    public void Dispose()
    {
        _disposed = true;
        if (_context is not null) _context.OfflineLicensesChanged -= OnOfflineLicensesChanged;
    }
}

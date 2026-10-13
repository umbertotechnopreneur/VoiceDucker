# Microsoft Store assets

## Upload files

| Partner Center field | PNG |
| --- | --- |
| Poster art | `poster-1440x2160.png` or `poster-720x1080.png` |
| Box art | `box-art-2160x2160.png` or `box-art-1080x1080.png` |
| App tile 300 | `app-tile-imagegen-300.png` |
| App tile 150 | `app-tile-imagegen-150.png` |
| App tile 71 | `app-tile-imagegen-71.png` |
| Main screenshot | `01-main-1440x2560.png` |
| Settings screenshot | `02-settings-1440x2560.png` |
| About screenshot | `03-about-1440x2560.png` |
| Playback screenshot | `04-playback-1440x2560.png` |
| Super hero art | `hero-1920x1080.png` |

The older `app-tile-*.png` exports are preserved alongside the new ImageGen variants.
Copy-ready supplemental fields: `supplemental-fields-en-US.txt`.
Other listing text and screenshot captions: `Store-copy-en-US.txt`.

## Sources and verification

`originals/` preserves every PNG from the session's generated-images folder, with original filenames. Original files remain in the Codex generation folder too. Copies were checked with SHA-256. `PNG-SHA256SUMS.txt` covers all current PNGs in this folder and its subfolders.

New poster, box art and icon use built-in ImageGen. Final prompts: `logo-imagegen-prompts.txt`. The source poster is 1024x1536; box art and icon are 1254x1254. Store exports are resized using high-quality bicubic interpolation without cropping or changing their aspect ratios; larger exports do not imply newly generated detail. All three icon sizes derive from one generated master. Existing application source icons are unchanged.

PNG dimensions and size limits were checked; poster/box/screenshot/hero files are below 50 MB, icons below 5 MB. The portal labels the poster slot 9:16, but its required 720x1080 and 1440x2160 pixel sizes are 2:3; exports follow the pixel sizes.

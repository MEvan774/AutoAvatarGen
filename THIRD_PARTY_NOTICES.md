# Third-party notices

Free fonts and data bundled with AutoAvatarGen, and where their terms live.

| What | Where in the project | Licence |
|---|---|---|
| **Inter** (Regular, Medium, SemiBold, Bold, ExtraBold) — the card font | `Assets/Resources/Fonts/Inter*.ttf` | SIL Open Font License 1.1 — `Assets/Resources/Fonts/Inter-OFL.txt` |
| **Noto Serif** (Regular, Bold) — the `{Document:}` card's paper face | `Assets/Resources/Fonts/NotoSerif-*.ttf` | SIL Open Font License 1.1 — `Assets/Resources/Fonts/NotoSerif-OFL.txt` |
| **Anton SC** (Regular) — the `{Echo:}` headline, the thumbnail's own face | `Assets/Resources/Fonts/AntonSC-Regular.ttf` (copied unchanged from the MugsTech repo's `ThumbnailEditor/assets/fonts/`) | SIL Open Font License 1.1 — `Assets/Resources/Fonts/AntonSC-OFL.txt` |
| **Natural Earth** 1:50m Admin-0 Countries — the `{Map:}` card's country shapes | source: `ThirdParty/NaturalEarth/ne_50m_admin_0_countries.geojson` (editor-only, not shipped); baked: `Assets/Resources/Maps/NaturalEarthRegions.bytes` | Public domain |

## Natural Earth

Made with Natural Earth. Free vector and raster map data @ naturalearthdata.com.

The source file is Natural Earth's `ne_50m_admin_0_countries.geojson` (v5.x), taken
from Natural Earth's own repository, github.com/nvkelso/natural-earth-vector. The
editor's **MugsTech ▸ Maps ▸ Bake Natural Earth Countries** turns it into the baked
asset (projected per region, simplified, triangulated). Nothing is downloaded at
runtime. Country names are not stored; borders shown are Natural Earth's and do not
imply any position on disputed territory.

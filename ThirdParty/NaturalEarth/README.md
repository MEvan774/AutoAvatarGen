# Natural Earth source data (editor-only)

`ne_50m_admin_0_countries.geojson` — Natural Earth 1:50m Admin-0 Countries, public
domain. Downloaded once, at authoring time, on 2026-11-08 from
`https://raw.githubusercontent.com/nvkelso/natural-earth-vector/master/geojson/ne_50m_admin_0_countries.geojson`
(3,083,490 bytes, SHA-256 `3e458fc036ad0a66411f2c1e6cac49c5d7bfb81cb1123bc513b22511a2b7fdeb`).

This folder sits outside `Assets/`, so Unity doesn't import it and builds never ship
it. **MugsTech ▸ Maps ▸ Bake Natural Earth Countries** reads it and writes
`Assets/Resources/Maps/NaturalEarthRegions.bytes`, which is what the `{Map:}` card
draws. Re-run the bake only when this file or `MapRegions.cs` changes.

Made with Natural Earth. Free vector and raster map data @ naturalearthdata.com.

using System;
using System.IO;
using System.Xml;
using System.Linq;
using System.Xml.Serialization;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using Property = System.Collections.Generic.KeyValuePair<string, string>;

/// <summary>
/// Represents a parsed Tiled map.
/// This class is strongly tied to the map.xsd generated classes, and will need to be updated whenever they are.
/// </summary>
public class Map
{
    //tmx file path/name info
    public string MapFileDir { get; protected set; }
    public string MapFileName { get; protected set; }
    public string MapFilePathName { get { return Path.Combine(MapFileDir, MapFileName); } }

    //deserialized tmx file contents
    private Tiled.map map;

    //GraphicsDevice for loading tilemap images
    private GraphicsDevice graphicsDevice;

    //standard map properties read from the tmx
    public int Width { get; protected set; }
    public int Height { get; protected set; }
    public int TileWidth { get; protected set; }
    public int TileHeight { get; protected set; }
    public int WidthPx { get { return Width * TileWidth; } }
    public int HeightPx { get { return Height * TileHeight; } }
    public string Version { get; protected set; }

    //map elements read from the tmx
    public List<Property> Properties { get; protected set; }
    public List<TileSet> TileSets { get; protected set; }
    public List<Layer> Layers { get; protected set; }
    public List<ObjectGroup> ObjectGroups { get; protected set; }

    //debug mode to draw gridlines and object positions
    public bool Debug { get; set; }

    public Map(string tmxFile, GraphicsDevice gd)
    {
        try
        {
            map = DeserializeTMX(tmxFile);
            ValidateMapAttributes();
            ValidateMapElements();

            MapFileDir = Path.GetDirectoryName(tmxFile);
            MapFileName = Path.GetFileName(tmxFile);
            graphicsDevice = gd;

            Width = int.Parse(map.width);
            Height = int.Parse(map.height);
            TileWidth = int.Parse(map.tilewidth);
            TileHeight = int.Parse(map.tileheight);
            Version = map.version;

            LoadMapElements();
        }
        catch (Exception e)
        {
            throw new Exception("Error loading map: " + e.Message, e);
        }
    }

    private void ValidateMapAttributes()
    {
        if (map.orientation != Tiled.orientationT.orthogonal) throw new Exception("This map loader only supports orthogonal orientation.");
        if (string.IsNullOrWhiteSpace(map.width)) throw new Exception("Map width is not specified.");
        if (string.IsNullOrWhiteSpace(map.height)) throw new Exception("Map height is not specified.");
        if (string.IsNullOrWhiteSpace(map.tilewidth)) throw new Exception("Map tile width is not specified.");
        if (string.IsNullOrWhiteSpace(map.tileheight)) throw new Exception("Map tile height is not specified.");
    }

    private void ValidateMapElements()
    {
        if (map.tileset == null || map.tileset.Length <= 0) throw new Exception("Map contains no tilesets.");
    }

    private void LoadMapElements()
    {
        Properties = LoadProperties(map.properties);
        TileSets = (from Tiled.tileset ts in map.tileset select new TileSet(MapFileDir, ts, graphicsDevice)).ToList();
        Layers = (from object o in map.Items where o.GetType() == typeof(Tiled.layer) select new Layer((Tiled.layer)o)).ToList();
        ObjectGroups = (from object o in map.Items where o.GetType() == typeof(Tiled.objectgroup) select new ObjectGroup((Tiled.objectgroup)o)).ToList();
    }

    public static List<Property> LoadProperties(Tiled.properties properties)
    {
        if (properties == null) return new List<Property>();
        return (from Tiled.property p in properties.property select new Property(p.name, p.value)).ToList();
    }

    private Tiled.map DeserializeTMX(string tmxFile)
    {
        XmlDocument doc = new XmlDocument();
        doc.Load(tmxFile);

        //TODO: validate against xsd

        //HACK: Tiled does not include the xmlns in .tmx files... set it here (must match namespace from map.xsd!)
        doc.DocumentElement.SetAttribute("xmlns", "http://mapeditor.org");

        Tiled.map map;
        XmlSerializer serializer = new XmlSerializer(typeof(Tiled.map));
        using (StringReader reader = new StringReader(doc.OuterXml))
        {
            map = (Tiled.map)serializer.Deserialize(reader);
        }

        return map;
    }

    //draw to the whole screen
    public void Draw(SpriteBatch sb)
    {
        Draw(sb, graphicsDevice.Viewport.Bounds);
    }

    //draw within the given bounds
    public void Draw(SpriteBatch sb, Rectangle viewWindowPx)
    {
        foreach (Layer layer in Layers)
        {
            //don't bother with invisible layers
            if (layer.Opacity <= 0) continue;

            //layer opacity (white means no color tinting in XNA)
            Color layerColor = Color.Lerp(Color.Transparent, Color.White, MathHelper.Clamp(layer.Opacity, 0, 1));

            //convert view position and dimensions from pixel to tile units
            Rectangle viewWindowTiles = new Rectangle();
            viewWindowTiles.X = viewWindowPx.X / TileWidth;
            viewWindowTiles.Y = viewWindowPx.Y / TileHeight;
            viewWindowTiles.Width = (int)Math.Ceiling(viewWindowPx.Width / (double)TileWidth);
            viewWindowTiles.Height = (int)Math.Ceiling(viewWindowPx.Height / (double)TileWidth);

            //possible partial tile offset
            int viewTileOffsetX = viewWindowPx.X % TileWidth;
            int viewTileOffsetY = viewWindowPx.Y % TileHeight;
            if (viewTileOffsetX > 0) viewWindowTiles.Width += 1;
            if (viewTileOffsetY > 0) viewWindowTiles.Height += 1;

            //draw each tile in the layer
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    Rectangle tileDestRect = new Rectangle((x - viewWindowTiles.X) * TileWidth - viewTileOffsetX,
                                                           (y - viewWindowTiles.Y) * TileHeight - viewTileOffsetY,
                                                           TileWidth, TileHeight);

                    //don't draw tiles we won't see
                    if (!viewWindowTiles.Contains(x, y)) continue;

                    Tile tile = layer.Tiles[x, y];
                    if (tile.GID == 0) continue;

                    //get the tileset that owns this tile, and the source rect within it
                    TileSet tileset;
                    Rectangle tileSrcRect;
                    TileSets.ResolveTileGID(tile.GID, out tileset, out tileSrcRect);

                    //flip and rotation settings
                    SpriteEffects flip = SpriteEffects.None;
                    float rotation = 0.0f;
                    if (!tile.FlippedDiagonally)
                    {
                        if (tile.FlippedHorizontally) flip |= SpriteEffects.FlipHorizontally;
                        if (tile.FlippedVertically) flip |= SpriteEffects.FlipVertically;
                    }
                    else
                    {
                        if (tile.FlippedHorizontally) rotation = MathHelper.PiOver2;
                        else rotation = -MathHelper.PiOver2;
                    }

                    if (!tile.FlippedDiagonally)
                    {
                        //no rotation, but possibly horizontally/vertically flipped
                        sb.Draw(tileset.Texture, tileDestRect, tileSrcRect, layerColor, 0.0f, Vector2.Zero, flip, 0);
                    }
                    else
                    {
                        //if tile is rotated, need to offset dest rect due to the way XNA draws things centered when rotated
                        Rectangle adjustedDestRect = new Rectangle(tileDestRect.X + tileDestRect.Width / 2, tileDestRect.Y + tileDestRect.Height / 2, TileWidth, TileHeight);
                        sb.Draw(tileset.Texture, adjustedDestRect, tileSrcRect, layerColor, rotation, new Vector2(TileWidth / 2, TileHeight / 2), flip, 0);
                    }
                }
            }
        }

        if (Debug)
        {
            DrawGridlines(sb, viewWindowPx);

            foreach (ObjectGroup objGroup in ObjectGroups)
            {
                foreach (Object obj in objGroup.Objects)
                {
                    Rectangle objRect = new Rectangle((int)(obj.X - viewWindowPx.X), 
                                                      (int)(obj.Y - viewWindowPx.Y), 
                                                      obj.Width, obj.Height);
                    Util.DrawRectangle(sb, objRect, Object.DEFAULT_COLOR);
                }
            }
        }
    }

    private void DrawGridlines(SpriteBatch sb, Rectangle viewWindowPx)
    {
        //borders; off-by-one for some because line size "grows" down and to the left
        Util.DrawLine(sb, 1.0f, new Vector2(1, 0), new Vector2(1, viewWindowPx.Height), Color.Black);
        Util.DrawLine(sb, 1.0f, new Vector2(viewWindowPx.Width, 0), new Vector2(viewWindowPx.Width, viewWindowPx.Height), Color.Black);
        Util.DrawLine(sb, 1.0f, new Vector2(0, 0), new Vector2(viewWindowPx.Width, 0), Color.Black);
        Util.DrawLine(sb, 1.0f, new Vector2(0, viewWindowPx.Height - 1), new Vector2(viewWindowPx.Width, viewWindowPx.Height - 1), Color.Black);

        //grid
        for (int x = 0; x < WidthPx; x += TileWidth)
        {
            Util.DrawLine(sb, 1.0f, new Vector2(x - viewWindowPx.X, 0), new Vector2(x - viewWindowPx.X, Math.Min(viewWindowPx.Height, HeightPx - viewWindowPx.Y)), Color.Black);
        }
        for (int y = 0; y < HeightPx; y += TileHeight)
        {
            Util.DrawLine(sb, 1.0f, new Vector2(0, y - viewWindowPx.Y), new Vector2(Math.Min(viewWindowPx.Width, WidthPx - viewWindowPx.X), y - viewWindowPx.Y), Color.Black);
        }
    }
}
using System;
using System.Collections.Generic;
using Candide;
using Candide.Database;
using Candide.Database.Doodad;
using CandideCreator.Shared;
using CandideCreator.Shared.Graphics;
using CandideCreator.Shared.Models;
using CandideCreator.Shared.Models.SpritesAnimationCollections;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace RomesteadRadar
{
    /// <summary>
    /// 为生物雷达生成 24×24 头像。
    /// 优先从生物的 Spac 动画里取“朝下（正面）”的第一帧；取不到时退回 doodad 贴图里的默认帧。
    /// 拿到帧后再裁掉透明边、缩放成图标注册进 IconDataBase。
    /// </summary>
    internal static class CreaturePortraits
    {
        private const int IconSize = 24;

        /// <summary>朝向索引：0=右、1=右下、2=下（正面朝向镜头）、3=左下、4=左……</summary>
        private const int FrontDirection = 2;

        private static readonly Dictionary<Guid, string> CachedIconIds = new Dictionary<Guid, string>();
        private static readonly HashSet<Guid> FailedGuids = new HashSet<Guid>();

        internal static string GetIconId(Guid baseGuid)
        {
            if (baseGuid == Guid.Empty)
                return null;
            if (CachedIconIds.TryGetValue(baseGuid, out string cached))
                return cached;
            if (FailedGuids.Contains(baseGuid))
                return null;

            try
            {
                BaseDoodad doodad = FindDoodad(baseGuid);
                string iconId = BuildIcon(baseGuid, doodad);
                if (iconId != null)
                {
                    CachedIconIds[baseGuid] = iconId;
                    return iconId;
                }
            }
            catch (Exception ex)
            {
                Plugin.LogSource?.LogWarning("Failed to build creature portrait: " + ex.Message);
            }

            FailedGuids.Add(baseGuid);
            return null;
        }

        private static BaseDoodad FindDoodad(Guid guid)
        {
            foreach (DoodadDatabase database in DoodadDatabaseManager.Databases.Values)
            {
                if (database.TryGetDoodad(guid, out BaseDoodad doodad))
                    return doodad;
            }
            if (DoodadDatabaseManager.BaseDoodads.TryGetValue(guid, out BaseDoodad baseDoodad))
                return baseDoodad;
            return null;
        }

        private static string BuildIcon(Guid baseGuid, BaseDoodad doodad)
        {
            if (Globals.GraphicsDevice == null)
                return null;

            Texture2D texture;
            int frameIndex;
            int frameWidth;
            int frameHeight;
            if (!TryGetFrontFrame(doodad, out texture, out frameIndex, out frameWidth, out frameHeight)
                && !TryGetDoodadFrame(doodad, out texture, out frameIndex, out frameWidth, out frameHeight))
                return null;

            int columns = Math.Max(1, texture.Width / frameWidth);
            int rows = Math.Max(1, texture.Height / frameHeight);
            frameIndex = frameIndex % (columns * rows);

            Color[] pixels = new Color[texture.Width * texture.Height];
            texture.GetData(pixels);

            // 有些生物（例如母山羊）默认帧是空的，这时退回到贴图里不透明像素最多的一格。
            Rectangle frame = FindBestFrame(pixels, texture.Width, texture.Height,
                columns, rows, frameWidth, frameHeight, frameIndex);

            Color[] portrait = BuildPortraitPixels(pixels, texture.Width, texture.Height, frame);
            if (portrait == null)
                return null;

            Texture2D portraitTexture = new Texture2D(Globals.GraphicsDevice, IconSize, IconSize);
            portraitTexture.SetData(portrait);

            SpriteSheet sheet = new SpriteSheet(portraitTexture, Content.DefaultNormalMap, Content.DefaultMetalMap);
            string iconId = "radar:creature:" + baseGuid.ToString("N");
            IconDataBase.AddIcons(new[]
            {
                new IconData
                {
                    Id = iconId,
                    Variations = new Dictionary<IconFlag, Icon>
                    {
                        { IconFlag.Small, new Icon(sheet, 0) },
                        { IconFlag.Medium, new Icon(sheet, 0) }
                    }
                }
            });
            return iconId;
        }

        /// <summary>优先用默认帧；默认帧为空时选贴图里不透明像素最多的一格，避免生成空图标。</summary>
        private static Rectangle FindBestFrame(Color[] pixels, int textureWidth, int textureHeight,
            int columns, int rows, int frameWidth, int frameHeight, int preferredIndex)
        {
            Rectangle preferred = CreateFrameRect(preferredIndex, columns, frameWidth, frameHeight, textureWidth, textureHeight);
            if (CountOpaquePixels(pixels, textureWidth, preferred) > 0)
                return preferred;

            Rectangle best = preferred;
            int bestCount = 0;
            int total = columns * rows;
            for (int index = 0; index < total; index++)
            {
                Rectangle frame = CreateFrameRect(index, columns, frameWidth, frameHeight, textureWidth, textureHeight);
                int count = CountOpaquePixels(pixels, textureWidth, frame);
                if (count > bestCount)
                {
                    bestCount = count;
                    best = frame;
                }
            }
            return best;
        }

        private static Rectangle CreateFrameRect(int index, int columns, int frameWidth, int frameHeight,
            int textureWidth, int textureHeight)
        {
            int x = index % columns * frameWidth;
            int y = index / columns * frameHeight;
            int width = Math.Min(frameWidth, textureWidth - x);
            int height = Math.Min(frameHeight, textureHeight - y);
            if (x < 0 || y < 0 || width <= 0 || height <= 0)
                return Rectangle.Empty;
            return new Rectangle(x, y, width, height);
        }

        private static int CountOpaquePixels(Color[] pixels, int textureWidth, Rectangle frame)
        {
            if (frame.Width <= 0 || frame.Height <= 0)
                return 0;

            int count = 0;
            for (int y = frame.Y; y < frame.Bottom; y++)
            {
                int row = y * textureWidth;
                for (int x = frame.X; x < frame.Right; x++)
                {
                    if (pixels[row + x].A > 12)
                        count++;
                }
            }
            return count;
        }

        /// <summary>用生物自己的 Spac 动画找正面帧，这样无论贴图行顺序如何都能取到脸。</summary>
        private static bool TryGetFrontFrame(BaseDoodad doodad, out Texture2D texture, out int frameIndex, out int frameWidth, out int frameHeight)
        {
            texture = null;
            frameIndex = 0;
            frameWidth = 0;
            frameHeight = 0;

            string doodadTextureName = doodad?.Texture?.Name;
            if (string.IsNullOrEmpty(doodadTextureName))
                return false;

            foreach (Spac spac in SpacDataBase.SpacMap.Values)
            {
                if (spac?.SpriteSheets == null || spac.SpritesAnimations == null || spac.SpritesAnimations.Length == 0)
                    continue;
                if (!SpacUsesTexture(spac, doodadTextureName))
                    continue;

                SpacDirectionsAnimation animation = FindAnimation(spac);
                if (animation.SpritesAnimations == null || animation.SpritesAnimations.Length == 0)
                    continue;

                int direction = FrontDirection % animation.SpritesAnimations.Length;
                SpacAnimation directionAnimation = animation.SpritesAnimations[direction];
                if (directionAnimation.Frames == null || directionAnimation.Frames.Length == 0)
                    continue;

                SpacFrame spacFrame = directionAnimation.Frames[0];
                if (spacFrame.Sprites == null || spacFrame.Sprites.Length == 0)
                    continue;

                // 优先取使用 doodad 主贴图的那一块（通常是身体），否则取第一个可见部件。
                SpacSprite? chosen = null;
                for (int i = 0; i < spacFrame.Sprites.Length; i++)
                {
                    SpacSprite sprite = spacFrame.Sprites[i];
                    if (sprite.Hidden)
                        continue;
                    SpriteSheet spriteSheet = GetSheet(spac, sprite.SpriteSheetIndex);
                    if (chosen == null)
                        chosen = sprite;
                    if (spriteSheet?.Texture?.Name == doodadTextureName)
                    {
                        chosen = sprite;
                        break;
                    }
                }

                if (!chosen.HasValue)
                    continue;

                SpriteSheet sheet = GetSheet(spac, chosen.Value.SpriteSheetIndex);
                if (sheet?.Texture == null || sheet.SpriteWidth <= 0 || sheet.SpriteHeight <= 0)
                    continue;

                texture = sheet.Texture;
                frameIndex = chosen.Value.Frame;
                frameWidth = sheet.SpriteWidth;
                frameHeight = sheet.SpriteHeight;
                return true;
            }

            return false;
        }

        /// <summary>没有 Spac 数据时的兜底：用 doodad 贴图。尺寸缺失时按行列数反推，避免算出空图标。</summary>
        private static bool TryGetDoodadFrame(BaseDoodad doodad, out Texture2D texture, out int frameIndex, out int frameWidth, out int frameHeight)
        {
            texture = doodad?.Texture?.Texture;
            if (texture == null && !string.IsNullOrEmpty(doodad?.Texture?.Name))
            {
                try
                {
                    texture = Content.Texture(doodad.Texture.Name);
                }
                catch (Exception)
                {
                    texture = null;
                }
            }

            frameIndex = doodad?.Frame ?? 0;
            frameWidth = (int)Math.Round(doodad?.SpriteWidth ?? 0f);
            frameHeight = (int)Math.Round(doodad?.SpriteHeight ?? 0f);
            if (texture == null)
                return false;

            int columns = Math.Max(1, doodad?.HorizontalFrames ?? 1);
            int rows = Math.Max(1, doodad?.VerticalFrames ?? 1);
            if (frameWidth <= 0 || frameWidth > texture.Width)
                frameWidth = Math.Max(1, texture.Width / columns);
            if (frameHeight <= 0 || frameHeight > texture.Height)
                frameHeight = Math.Max(1, texture.Height / rows);

            return frameWidth > 0 && frameHeight > 0;
        }

        private static bool SpacUsesTexture(Spac spac, string textureName)
        {
            for (int i = 0; i < spac.SpriteSheets.Length; i++)
                if (spac.SpriteSheets[i]?.Texture?.Name == textureName)
                    return true;
            return false;
        }

        private static SpriteSheet GetSheet(Spac spac, int index)
        {
            if (index < 0 || index >= spac.SpriteSheets.Length)
                return null;
            return spac.SpriteSheets[index];
        }

        private static SpacDirectionsAnimation FindAnimation(Spac spac)
        {
            SpacDirectionsAnimation fallback = spac.SpritesAnimations[0];
            for (int i = 0; i < spac.SpritesAnimations.Length; i++)
            {
                string name = spac.SpritesAnimations[i].AnimationName;
                if (string.Equals(name, "idle", StringComparison.OrdinalIgnoreCase))
                    return spac.SpritesAnimations[i];
                if (string.Equals(name, "alert", StringComparison.OrdinalIgnoreCase))
                    fallback = spac.SpritesAnimations[i];
            }
            return fallback;
        }

        /// <summary>裁剪正面帧的透明边，等比缩放到 24×24 并居中。</summary>
        private static Color[] BuildPortraitPixels(Color[] pixels, int textureWidth, int textureHeight, Rectangle frame)
        {
            int minX = frame.Right;
            int minY = frame.Bottom;
            int maxX = frame.X - 1;
            int maxY = frame.Y - 1;

            for (int y = frame.Y; y < frame.Bottom; y++)
            {
                int row = y * textureWidth;
                for (int x = frame.X; x < frame.Right; x++)
                {
                    if (pixels[row + x].A <= 12)
                        continue;
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }

            if (maxX < minX || maxY < minY)
                return null;

            int sourceWidth = maxX - minX + 1;
            int sourceHeight = maxY - minY + 1;
            float scale = Math.Min((IconSize - 2f) / sourceWidth, (IconSize - 2f) / sourceHeight);
            int targetWidth = Math.Max(1, (int)Math.Round(sourceWidth * scale));
            int targetHeight = Math.Max(1, (int)Math.Round(sourceHeight * scale));
            int offsetX = (IconSize - targetWidth) / 2;
            int offsetY = (IconSize - targetHeight) / 2;

            Color[] result = new Color[IconSize * IconSize];
            for (int y = 0; y < targetHeight; y++)
            {
                float sourceY = minY + (y + 0.5f) * sourceHeight / targetHeight - 0.5f;
                for (int x = 0; x < targetWidth; x++)
                {
                    float sourceX = minX + (x + 0.5f) * sourceWidth / targetWidth - 0.5f;
                    result[(offsetY + y) * IconSize + offsetX + x] =
                        SampleBilinear(pixels, textureWidth, textureHeight, sourceX, sourceY);
                }
            }
            return result;
        }

        private static Color SampleBilinear(Color[] pixels, int width, int height, float x, float y)
        {
            int x0 = (int)Math.Floor(x);
            int y0 = (int)Math.Floor(y);
            int x1 = x0 + 1;
            int y1 = y0 + 1;
            float fx = x - x0;
            float fy = y - y0;

            x0 = Clamp(x0, 0, width - 1);
            x1 = Clamp(x1, 0, width - 1);
            y0 = Clamp(y0, 0, height - 1);
            y1 = Clamp(y1, 0, height - 1);

            Color c00 = pixels[y0 * width + x0];
            Color c10 = pixels[y0 * width + x1];
            Color c01 = pixels[y1 * width + x0];
            Color c11 = pixels[y1 * width + x1];

            // 注意：Color 的这个构造函数参数是 0~1 的浮点；直接传 0~255 的通道值会被截断成纯白。
            return new Color(
                (int)Math.Round(Lerp(Lerp(c00.R, c10.R, fx), Lerp(c01.R, c11.R, fx), fy)),
                (int)Math.Round(Lerp(Lerp(c00.G, c10.G, fx), Lerp(c01.G, c11.G, fx), fy)),
                (int)Math.Round(Lerp(Lerp(c00.B, c10.B, fx), Lerp(c01.B, c11.B, fx), fy)),
                (int)Math.Round(Lerp(Lerp(c00.A, c10.A, fx), Lerp(c01.A, c11.A, fx), fy)));
        }

        private static float Lerp(float a, float b, float t)
        {
            return a + (b - a) * t;
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
    }
}

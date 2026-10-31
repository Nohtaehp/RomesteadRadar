using System;
using Candide;
using Candide.CandideUI.UiInput;
using Candide.GameModels;
using Candide.Graphics;
using Candide.Graphics.Fonts;
using Candide.World;
using CandideServer.Models.Worlds;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using RomesteadRadar;
using HarmonyLib;

namespace RomesteadRadar.Patches
{
    /// <summary>
    /// 在小地图的绘制批次里追加雷达波。
    /// WorldMap.Draw 的小地图分支会以 WorldMapContext.Minimap 调用这个方法，
    /// 此时 SpriteBatch 已经 Begin 且裁剪区域就是小地图，直接画即可。
    /// </summary>
    [HarmonyPatch(typeof(WorldMap), "DrawExteriorMap", new[]
    {
        typeof(SpriteBatch),
        typeof(Vector2),
        typeof(Vector2),
        typeof(float),
        typeof(Vector2),
        typeof(float)
    })]
    internal static class MinimapRadarPatch
    {
        private static Rectangle _lastMinimapRect;
        private static bool _hasMinimapRect;
        private static bool _labelErrorLogged;

        private static void Postfix(WorldMap __instance, SpriteBatch batch, Vector2 position, Vector2 size, float scale, Vector2 centerTile)
        {
            if (__instance.Context != WorldMap.WorldMapContext.Minimap)
                return;

            _lastMinimapRect = new Rectangle((int)position.X, (int)position.Y, (int)size.X, (int)size.Y);
            _hasMinimapRect = true;

            ItemRadar.DrawMinimapPulse(__instance, batch, position, size, scale, centerTile);
        }

        /// <summary>在地图绘制结束、批次已经 End 之后，在右下角补上追踪目标文字。</summary>
        internal static void DrawTrackingLabel(SpriteBatch batch)
        {
            if (!Plugin.RadarEnabled || !_hasMinimapRect || !ItemRadar.HasTarget)
                return;
            if (GameState.CurrentWorld == null || GameState.CurrentWorld.Type == WorldModel.WorldType.Interior)
                return;

            string name = ItemRadar.TargetDisplayName;
            if (string.IsNullOrEmpty(name))
                name = ItemRadar.TargetItemId;
            if (string.IsNullOrEmpty(name))
                return;

            string text = Loc.T("正在追踪:", "Tracking: ") + name;

            try
            {
                float uiScale = CandideUiSystem.Scale;
                var textScale = new Vector2(0.6f * uiScale);
                var font = FontManager.OculiOutline.RenderFont;
                Vector2 textSize = font.MeasureString(text, textScale);
                Vector2 position = new Vector2(
                    _lastMinimapRect.X + _lastMinimapRect.Width - textSize.X,
                    _lastMinimapRect.Y + _lastMinimapRect.Height + 6f * uiScale);

                batch.Begin(SpriteSortMode.Deferred, depthStencilState: DepthStencilState.Default,
                    samplerState: SamplerState.LinearClamp, blendState: Globals.CorrectBlend);
                try
                {
                    font.DrawText(batch, text, position, StyleHelper.TextYellowColor, 0f, Vector2.Zero, textScale);
                }
                finally
                {
                    batch.End();
                }
            }
            catch (Exception ex)
            {
                if (!_labelErrorLogged)
                {
                    _labelErrorLogged = true;
                    Plugin.LogSource?.LogWarning("Failed to draw tracking label: " + ex.Message);
                }
            }
        }
    }

    /// <summary>WorldMap.Draw 结束时绘制追踪文字（此时小地图批次已结束，不受小地图裁剪限制）。</summary>
    [HarmonyPatch(typeof(WorldMap), "Draw")]
    internal static class MinimapTrackingLabelPatch
    {
        private static void Postfix(WorldMap __instance, SpriteBatch batch)
        {
            if (__instance.Mode != WorldMap.WorldMapMode.Minimap)
                return;
            MinimapRadarPatch.DrawTrackingLabel(batch);
        }
    }
}

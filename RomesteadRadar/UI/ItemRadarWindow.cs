using System;
using System.Collections.Generic;
using Candide;
using Candide.CandideUI;
using Candide.CandideUI.Components;
using Candide.CandideUI.Components.Buttons;
using Candide.CandideUI.Containers;
using Candide.CandideUI.JobUiNew.Components;
using Candide.GameModels;
using CandideCreator.Shared.Graphics;
using Microsoft.Xna.Framework;
using RomesteadRadar;
using Shared.Data;
using Shared.Models.Items;

namespace RomesteadRadar.UI
{
    /// <summary>
    /// 顶栏雷达窗口。可在“祭坛献祭过的物品”和“自己杀死过的生物”两种模式间切换；
    /// 献祭过箱子后，物品列表顶部会出现“未开启的宝箱”条目。
    /// 选中一项后由 <see cref="ItemRadar"/> 在小地图上显示扇形方向波。
    /// </summary>
    internal static class ItemRadarWindow
    {
        private enum RadarMode
        {
            Items,
            Creatures
        }

        /// <summary>祭坛献祭这个物品后，物品雷达里才会出现“未开启的宝箱”条目。</summary>
        private const string ChestItemId = "furniture:chest";

        private static CandideExtendedWindow _window;
        private static RadarMode _mode = RadarMode.Items;
        private static Action _refreshWindow;

        /// <summary>游戏内动态切换语言后，刷新窗口里已经显示的文字。</summary>
        internal static void OnLanguageChanged()
        {
            if (_window == null)
                return;
            _refreshWindow?.Invoke();
        }

        internal static void Open(CandideWindow owner)
        {
            try
            {
                OpenInternal(owner);
            }
            catch (Exception ex)
            {
                Plugin.LogSource?.LogError("Failed to open radar window: " + ex);
            }
        }

        private static void OpenInternal(CandideWindow owner)
        {
            CandideDesktop desktop = owner?.Desktop;
            if (desktop == null && Globals.Game?.ModeManager != null)
                desktop = Globals.Game.ModeManager.CandideDesktop;
            if (desktop == null)
                return;

            // 再点一次 = 关闭。
            if (_window != null)
            {
                _window.Close();
                _window = null;
                return;
            }

            // 保留顶部选项栏，关闭地图/背包等其它窗口。
            PlayerUi.CloseAllNonOverlay(desktop, skipTopBar: true);
            PlayerUi.EnsureTopBarVisible(desktop);

            var window = new CandideExtendedWindow
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                CloseOnUiClosedPress = true
            };

            var root = new CandideVerticalStackPanel
            {
                Width = 400,
                Padding = new CandideThickness(16, 10, 16, 14)
            };

            var titleLabel = new CandideTextLabel
            {
                Text = TitleForMode(_mode),
                Margin = new CandideThickness(0, 0, 0, 4)
            };
            root.AddChild(titleLabel);

            var instructionLabel = new CandideTextLabel
            {
                Text = InstructionText(),
                Wrap = true,
                Width = 368,
                Margin = new CandideThickness(0, 0, 0, 8)
            };
            root.AddChild(instructionLabel);

            var status = new CandideTextLabel
            {
                Text = BuildStatusText(),
                Wrap = true,
                Width = 368,
                Margin = new CandideThickness(0, 6, 0, 6)
            };

            var list = new CandideListView
            {
                MaxHeight = 320,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new CandideThickness(0, 0, 0, 6)
            };

            void SelectRow(string tag)
            {
                if (string.IsNullOrEmpty(tag))
                    return;

                if (tag.StartsWith("item|", StringComparison.Ordinal))
                {
                    string itemId = tag.Substring(5);
                    ItemRadar.SetItemTarget(itemId);
                }
                else if (tag.StartsWith("creature|", StringComparison.Ordinal))
                {
                    string guidText = tag.Substring(9);
                    if (!Guid.TryParse(guidText, out Guid baseGuid))
                        return;
                    ItemRadar.SetCreatureTarget(baseGuid);
                }
                else if (tag.StartsWith("chest|", StringComparison.Ordinal))
                {
                    ItemRadar.SetChestTarget();
                }
                else
                {
                    return;
                }

                status.Text = BuildStatusText();
            }

            void Rebuild()
            {
                list.ClearAllItems();

                bool itemMode = _mode == RadarMode.Items;
                List<RadarRow> currentRows = itemMode ? BuildOfferedRows() : BuildCreatureRows();

                if (currentRows.Count == 0)
                {
                    string hint = itemMode
                        ? Loc.T("没有可追踪的献祭物品。", "No offered items available for tracking.")
                        : Loc.T(
                            "还没有击杀记录。杀死生物后，它会出现在这里（记录会保存）。",
                            "No kill records yet. Kill a creature and it will appear here (records are saved).");
                    list.AddNonItem(new CandideTextLabel
                    {
                        Text = hint,
                        Wrap = true,
                        Width = 340,
                        Margin = new CandideThickness(4, 8, 4, 8)
                    });
                }
                else
                {
                    for (int i = 0; i < currentRows.Count; i++)
                    {
                        RadarRow row = currentRows[i];
                        string tag = row.IsCreature
                            ? "creature|" + row.CreatureGuid.ToString("D")
                            : row.IsChest ? "chest|track" : "item|" + row.ItemId;

                        var listItem = new CandideRecipeListItem(row.Icon ?? "bjorn", IconFlag.Medium)
                        {
                            Text = row.Name,
                            Tag = tag,
                            HorizontalAlignment = HorizontalAlignment.Stretch,
                            Margin = new CandideThickness(0, 0, 9, 0)
                        };
                        list.AddItem(listItem);

                        if (!ItemRadar.HasTarget)
                            continue;

                        if (itemMode && row.IsChest
                            && ItemRadar.CurrentTargetKind == ItemRadar.TargetKind.Chest)
                        {
                            list.SelectedItem = listItem;
                        }
                        else if (itemMode && !row.IsChest && ItemRadar.CurrentTargetKind == ItemRadar.TargetKind.Item
                            && ItemRadar.TargetItemId == row.ItemId)
                        {
                            list.SelectedItem = listItem;
                        }
                        else if (!itemMode && ItemRadar.CurrentTargetKind == ItemRadar.TargetKind.Creature
                            && ItemRadar.TargetCreatureGuid == row.CreatureGuid)
                        {
                            list.SelectedItem = listItem;
                        }
                    }
                }

                status.Text = BuildStatusText();
            }

            list.SelectedChanged += delegate(object sender, CandideUiElement element)
            {
                string tag = element?.Tag;
                if (string.IsNullOrEmpty(tag))
                    return;

                RadarRow row = FindRow(tag);
                if (row == null)
                    return;

                SelectRow(tag);
            };

            RadarRow FindRow(string tag)
            {
                if (tag.StartsWith("item|", StringComparison.Ordinal))
                {
                    string itemId = tag.Substring(5);
                    List<RadarRow> itemRows = BuildOfferedRows();
                    for (int i = 0; i < itemRows.Count; i++)
                    {
                        if (!itemRows[i].IsChest && itemRows[i].ItemId == itemId)
                            return itemRows[i];
                    }
                }
                else if (tag.StartsWith("creature|", StringComparison.Ordinal))
                {
                    string guidText = tag.Substring(9);
                    if (Guid.TryParse(guidText, out Guid baseGuid))
                    {
                        List<RadarRow> creatureRows = BuildCreatureRows();
                        for (int i = 0; i < creatureRows.Count; i++)
                        {
                            if (creatureRows[i].CreatureGuid == baseGuid)
                                return creatureRows[i];
                        }
                    }
                }
                else if (tag.StartsWith("chest|", StringComparison.Ordinal))
                {
                    List<RadarRow> itemRows = BuildOfferedRows();
                    for (int i = 0; i < itemRows.Count; i++)
                    {
                        if (itemRows[i].IsChest)
                            return itemRows[i];
                    }
                }
                return null;
            }

            root.AddChild(list);
            root.AddChild(status);

            var stopButton = new CandideLabelButton(160)
            {
                Text = StopTrackingLabel(),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new CandideThickness(0, 4, 0, 4),
                Padding = new CandideThickness(6, 4, 6, 4)
            };
            stopButton.OnClickAction = delegate
            {
                ItemRadar.ClearTarget();
                list.SelectedItem = null;
                status.Text = Loc.T("已停止追踪。", "Tracking stopped.");
            };
            root.AddChild(stopButton);

            var modeButton = new CandideLabelButton(160)
            {
                Text = ModeSwitchLabel(_mode),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new CandideThickness(0, 4, 0, 0),
                Padding = new CandideThickness(6, 4, 6, 4)
            };
            modeButton.OnClickAction = delegate
            {
                _mode = NextMode(_mode);
                titleLabel.Text = TitleForMode(_mode);
                modeButton.Text = ModeSwitchLabel(_mode);
                ItemRadar.ClearTarget();
                Rebuild();
                status.Text = Loc.T("已切换模式。请选择追踪目标。", "Mode switched. Select a target to track.");
            };
            root.AddChild(modeButton);

            _refreshWindow = delegate
            {
                titleLabel.Text = TitleForMode(_mode);
                instructionLabel.Text = InstructionText();
                stopButton.Text = StopTrackingLabel();
                modeButton.Text = ModeSwitchLabel(_mode);
                Rebuild();
            };

            window.SetChild(root);
            window.Show(desktop);
            window.RequestFocus();

            _window = window;
            window.Closed += delegate
            {
                _window = null;
                _refreshWindow = null;
            };

            Rebuild();
        }

        private static List<RadarRow> BuildOfferedRows()
        {
            var rows = new List<RadarRow>();
            foreach (KeyValuePair<string, int> pair in PlayerGameState.WorshipOfferedItemAmounts)
            {
                // 宝箱条目单独置顶，避免在献祭列表里重复出现。
                if (pair.Key == ChestItemId)
                    continue;

                // 没有采集/掉落来源的物品不显示（清单见 雷达目标清单.html）。
                if (!RadarTargeting.HasAnySource(pair.Key))
                    continue;

                ItemData itemData = ItemDataBase.GetItemDataOrNull(pair.Key);
                string name = ItemRadar.GetItemDisplayName(pair.Key);

                rows.Add(new RadarRow
                {
                    ItemId = pair.Key,
                    Name = name,
                    Icon = itemData?.Icon
                });
            }

            rows.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));

            // 献祭过箱子后解锁“未开启的宝箱”，固定放在物品列表最上面。
            if (IsChestRadarUnlocked())
            {
                // 直接复用箱子物品自己的图标（furniture:chest），避免用到别的“chest”图标。
                ItemData chestItem = ItemDataBase.GetItemDataOrNull(ChestItemId);
                rows.Insert(0, new RadarRow
                {
                    ItemId = ChestItemId,
                    Name = Loc.T("未开启的宝箱", "Unopened Chest"),
                    Icon = chestItem?.Icon ?? "furniture:chest",
                    IsChest = true
                });
            }
            return rows;
        }

        /// <summary>箱子雷达是隐藏解锁项：只在祭坛献祭过箱子后出现。</summary>
        private static bool IsChestRadarUnlocked()
        {
            if (PlayerGameState.WorshipOfferedItemAmounts == null)
                return false;
            return PlayerGameState.WorshipOfferedItemAmounts.TryGetValue(ChestItemId, out int offered)
                && offered > 0;
        }

        private static List<RadarRow> BuildCreatureRows()
        {
            var rows = new List<RadarRow>();
            foreach (KilledCreature entry in KillTracker.GetEntries())
            {
                rows.Add(new RadarRow
                {
                    CreatureGuid = entry.BaseGuid,
                    Name = entry.Name,
                    Icon = CreaturePortraits.GetIconId(entry.BaseGuid) ?? "bjorn",
                    IsCreature = true
                });
            }
            return rows;
        }

        private static string TitleForMode(RadarMode mode)
        {
            return mode == RadarMode.Items
                ? Loc.T("物品雷达", "Item Radar")
                : Loc.T("生物雷达", "Creature Radar");
        }

        private static string InstructionText()
        {
            return Loc.T(
                "选择后移动时小地图会显示扇形方向波。",
                "Move after selecting a target to see the directional pulse on the minimap.");
        }

        private static string StopTrackingLabel()
        {
            return Loc.T("停止追踪", "Stop Tracking");
        }

        private static RadarMode NextMode(RadarMode mode)
        {
            return mode == RadarMode.Items ? RadarMode.Creatures : RadarMode.Items;
        }

        private static string ModeSwitchLabel(RadarMode mode)
        {
            return mode == RadarMode.Items
                ? Loc.T("切换到生物雷达", "Creature Radar")
                : Loc.T("切换到物品雷达", "Item Radar");
        }

        private static string BuildStatusText()
        {
            if (!ItemRadar.HasTarget)
                return Loc.T(
                    "尚未选择追踪目标。选择后需要保持移动，雷达才会出现。",
                    "No target selected. Keep moving after selecting one to make the radar appear.");

            string name = ItemRadar.TargetDisplayName;
            if (string.IsNullOrEmpty(name))
                name = ItemRadar.TargetItemId ?? string.Empty;
            string kindLabel;
            switch (ItemRadar.CurrentTargetKind)
            {
                case ItemRadar.TargetKind.Creature:
                    kindLabel = Loc.T("正在追踪生物：", "Tracking creature: ");
                    break;
                case ItemRadar.TargetKind.Chest:
                    kindLabel = Loc.T("正在追踪宝箱：", "Tracking chest: ");
                    break;
                default:
                    kindLabel = Loc.T("正在追踪物品：", "Tracking item: ");
                    break;
            }
            return kindLabel + name;
        }

        private sealed class RadarRow
        {
            public string ItemId;
            public Guid CreatureGuid;
            public string Name;
            public string Icon;
            public bool IsCreature;
            public bool IsChest;
        }
    }
}

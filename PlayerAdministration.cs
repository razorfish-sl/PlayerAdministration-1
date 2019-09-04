﻿/* --- Contributor information ---
 * Please follow the following set of guidelines when working on this plugin,
 * this to help others understand this file more easily.
 *
 * -- Authors --
 * Thimo (ThibmoRozier) <thibmorozier@live.nl>
 * rfc1920 <no@email.com>
 * Mheetu <no@email.com>
 *
 * -- Naming --
 * Avoid using non-alphabetic characters, eg: _
 * Avoid using numbers in method and class names (Upgrade methods are allowed to have these, for readability)
 * Private constants -------------------- SHOULD start with a uppercase "C" (PascalCase)
 * Private readonly fields -------------- SHOULD start with a uppercase "C" (PascalCase)
 * Private fields ----------------------- SHOULD start with a uppercase "F" (PascalCase)
 * Arguments/Parameters ----------------- SHOULD start with a lowercase "a" (camelCase)
 * Classes ------------------------------ SHOULD start with a uppercase character (PascalCase)
 * Methods ------------------------------ SHOULD start with a uppercase character (PascalCase)
 * Public properties (constants/fields) - SHOULD start with a uppercase character (PascalCase)
 * Variables ---------------------------- SHOULD start with a lowercase character (camelCase)
 *
 * -- Style --
 * Max-line-width ------- 160
 * Single-line comments - // Single-line comment
 * Multi-line comments -- Just like this comment block!
 */
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using Oxide.Game.Rust.Libraries;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using UnityEngine;
using RustLib = Oxide.Game.Rust.Libraries.Rust;

namespace Oxide.Plugins
{
    [Info("PlayerAdministration", "ThibmoRozier", "1.5.7")]
    [Description("Allows server admins to moderate users using a GUI from within the game.")]
    public class PlayerAdministration : CovalencePlugin
    {
        #region Plugin References
#pragma warning disable IDE0044, CS0649
        [PluginReference]
        private Plugin Economics;
        [PluginReference]
        private Plugin ServerRewards;
        [PluginReference]
        private Plugin Freeze;
        [PluginReference]
        private Plugin PermissionsManager;
        [PluginReference]
        private Plugin DiscordMessages;
        [PluginReference]
        private Plugin BetterChatMute;
#pragma warning restore IDE0044, CS0649
        #endregion Plugin References

        #region Library Imports
        private readonly RustLib rust = Interface.Oxide.GetLibrary<RustLib>();
        private readonly Player Player = Interface.Oxide.GetLibrary<Player>();
        #endregion Library Imports

        #region GUI
        #region Types
        /// <summary>
        /// UI Color object
        /// </summary>
        private class CuiColor
        {
            public byte R { get; set; }
            public byte G { get; set; }
            public byte B { get; set; }
            public float A { get; set; }

            public CuiColor(byte aRed = 255, byte aGreen = 255, byte aBlue = 255, float aAlpha = 1f)
            {
                R = aRed;
                G = aGreen;
                B = aBlue;
                A = aAlpha;
            }

            public override string ToString() => $"{(double)R / 255} {(double)G / 255} {(double)B / 255} {A}";

            public static readonly CuiColor Background = new CuiColor(240, 240, 240, 0.3f);
            public static readonly CuiColor BackgroundMedium = new CuiColor(76, 74, 72, 0.83f);
            public static readonly CuiColor BackgroundDark = new CuiColor(42, 42, 42, 0.93f);
            public static readonly CuiColor Button = new CuiColor(42, 42, 42, 1f);
            public static readonly CuiColor ButtonInactive = new CuiColor(168, 168, 168, 1f);
            public static readonly CuiColor ButtonDecline = new CuiColor(192, 0, 0, 1f);
            public static readonly CuiColor ButtonDanger = new CuiColor(193, 46, 42, 1f);
            public static readonly CuiColor ButtonWarning = new CuiColor(213, 133, 18, 1f);
            public static readonly CuiColor ButtonSuccess = new CuiColor(57, 132, 57, 1f);
            public static readonly CuiColor Text = new CuiColor(0, 0, 0, 1f);
            public static readonly CuiColor TextAlt = new CuiColor(255, 255, 255, 1f);
            public static readonly CuiColor TextTitle = new CuiColor(206, 66, 43, 1f);
            public static readonly CuiColor None = new CuiColor(0, 0, 0, 0f);
        }

        /// <summary>
        /// Element position object
        /// </summary>
        private class CuiPoint
        {
            public float X { get; set; }
            public float Y { get; set; }

            public CuiPoint(float aX = 0f, float aY = 0f)
            {
                X = aX;
                Y = aY;
            }

            public override string ToString() => $"{X} {Y}";

            public static readonly CuiPoint Zero = new CuiPoint();
        }

        /// <summary>
        /// UI pages to make the switching more humanly readable
        /// </summary>
        private enum UiPage
        {
            Main = 0,
            PlayersOnline,
            PlayersOffline,
            PlayersBanned,
            PlayerPage,
            PlayerPageBanned
        }
        #endregion Types

        #region UI object definitions
        /// <summary>
        /// Input field object
        /// </summary>
        private class CuiInputField
        {
            public CuiInputFieldComponent InputField { get; } = new CuiInputFieldComponent();
            public CuiRectTransformComponent RectTransform { get; } = new CuiRectTransformComponent();
            public float FadeOut { get; set; }
        }
        #endregion UI object definitions

        #region Component container
        /// <summary>
        /// Custom version of the CuiElementContainer to add InputFields
        /// </summary>
        private class CustomCuiElementContainer : CuiElementContainer
        {
            public string Add(CuiInputField aInputField, string aParent = Cui.ParentHud, string aName = "")
            {
                if (string.IsNullOrEmpty(aName))
                    aName = CuiHelper.GetGuid();

                if (aInputField == null) {
                    FPluginInstance.LogError($"CustomCuiElementContainer::Add > Parameter 'aInputField' is null");
                    return string.Empty;
                }

                Add(new CuiElement {
                    Name = aName,
                    Parent = aParent,
                    FadeOut = aInputField.FadeOut,
                    Components = {
                        aInputField.InputField,
                        aInputField.RectTransform
                    }
                });
                return aName;
            }
        }
        #endregion Component container

        /// <summary>
        /// Rust UI object
        /// </summary>
        private class Cui
        {
            public const string ParentHud = "Hud";
            public const string ParentOverlay = "Overlay";

            private readonly CustomCuiElementContainer FContainer = new CustomCuiElementContainer();
            private readonly BasePlayer FPlayer;
            public readonly ulong PlayerId;
            public readonly string PlayerIdString;

            /// <summary>
            /// Constructor
            /// </summary>
            /// <param name="aPlayer">The player this object is meant for</param>
            public Cui(BasePlayer aPlayer)
            {
                if (aPlayer == null) {
                    FPluginInstance.LogError("Cui::Cui > Parameter 'aPlayer' is null");
                    return;
                }

                FPlayer = aPlayer;
                PlayerId = aPlayer.userID;
                PlayerIdString = aPlayer.UserIDString;
                FPluginInstance.LogDebug("Cui instance created");
            }

            /// <summary>
            /// Add a new panel
            /// </summary>
            /// <param name="aParent">The parent object name</param>
            /// <param name="aLeftBottomAnchor">Left(x)-Bottom(y) relative position</param>
            /// <param name="aRightTopAnchor">Right(x)-Top(y) relative position</param>
            /// <param name="aIndCursorEnabled">The panel requires the cursor</param>
            /// <param name="aColor">Image color</param>
            /// <param name="aName">The object's name</param>
            /// <param name="aPng">Image PNG file path</param>
            /// <returns>New object name</returns>
            public string AddPanel(
                string aParent, CuiPoint aLeftBottomAnchor, CuiPoint aRightTopAnchor, bool aIndCursorEnabled, CuiColor aColor = null, string aName = "",
                string aPng = ""
            ) => AddPanel(aParent, aLeftBottomAnchor, aRightTopAnchor, CuiPoint.Zero, CuiPoint.Zero, aIndCursorEnabled, aColor, aName, aPng);

            /// <summary>
            /// Add a new panel
            /// </summary>
            /// <param name="aParent">The parent object name</param>
            /// <param name="aLeftBottomAnchor">Left(x)-Bottom(y) relative position</param>
            /// <param name="aRightTopAnchor">Right(x)-Top(y) relative position</param>
            /// <param name="aLeftBottomOffset">Left(x)-Bottom(y) relative offset</param>
            /// <param name="aRightTopOffset">Right(x)-Top(y) relative offset</param>
            /// <param name="aIndCursorEnabled">The panel requires the cursor</param>
            /// <param name="aColor">Image color</param>
            /// <param name="aName">The object's name</param>
            /// <param name="aPng">Image PNG file path</param>
            /// <returns>New object name</returns>
            public string AddPanel(
                string aParent, CuiPoint aLeftBottomAnchor, CuiPoint aRightTopAnchor, CuiPoint aLeftBottomOffset, CuiPoint aRightTopOffset,
                bool aIndCursorEnabled, CuiColor aColor = null, string aName = "", string aPng = ""
            ) {
                if (aLeftBottomAnchor == null || aRightTopAnchor == null || aLeftBottomOffset == null || aRightTopOffset == null) {
                    FPluginInstance.LogError($"Cui::AddPanel > One of the required parameters is null");
                    return string.Empty;
                }

                CuiPanel panel = new CuiPanel
                {
                    RectTransform =
                    {
                        AnchorMin = aLeftBottomAnchor.ToString(),
                        AnchorMax = aRightTopAnchor.ToString(),
                        OffsetMin = aLeftBottomOffset.ToString(),
                        OffsetMax = aRightTopOffset.ToString()
                    },
                    CursorEnabled = aIndCursorEnabled
                };

                if (!string.IsNullOrEmpty(aPng))
                    panel.Image = new CuiImageComponent { Png = aPng };

                if (aColor != null) {
                    if (panel.Image == null) {
                        panel.Image = new CuiImageComponent { Color = aColor.ToString() };
                    } else {
                        panel.Image.Color = aColor.ToString();
                    }
                }

                FPluginInstance.LogDebug("Added panel to container");
                return FContainer.Add(panel, aParent, string.IsNullOrEmpty(aName) ? null : aName);
            }

            /// <summary>
            /// Add a new label
            /// </summary>
            /// <param name="aParent">The parent object name</param>
            /// <param name="aLeftBottomAnchor">Left(x)-Bottom(y) relative position</param>
            /// <param name="aRightTopAnchor">Right(x)-Top(y) relative position</param>
            /// <param name="aColor">Text color</param>
            /// <param name="aText">Text to show</param>
            /// <param name="aName">The object's name</param>
            /// <param name="aFontSize">Font size</param>
            /// <param name="aAlign">Text alignment</param>
            /// <returns>New object name</returns>
            public string AddLabel(
                string aParent, CuiPoint aLeftBottomAnchor, CuiPoint aRightTopAnchor, CuiColor aColor, string aText, string aName = "", int aFontSize = 14,
                TextAnchor aAlign = TextAnchor.UpperLeft
            ) => AddLabel(aParent, aLeftBottomAnchor, aRightTopAnchor, CuiPoint.Zero, CuiPoint.Zero, aColor, aText, aName, aFontSize, aAlign);

            /// <summary>
            /// Add a new label
            /// </summary>
            /// <param name="aParent">The parent object name</param>
            /// <param name="aLeftBottomAnchor">Left(x)-Bottom(y) relative position</param>
            /// <param name="aRightTopAnchor">Right(x)-Top(y) relative position</param>
            /// <param name="aLeftBottomOffset">Left(x)-Bottom(y) relative offset</param>
            /// <param name="aRightTopOffset">Right(x)-Top(y) relative offset</param>
            /// <param name="aColor">Text color</param>
            /// <param name="aText">Text to show</param>
            /// <param name="aName">The object's name</param>
            /// <param name="aFontSize">Font size</param>
            /// <param name="aAlign">Text alignment</param>
            /// <returns>New object name</returns>
            public string AddLabel(
                string aParent, CuiPoint aLeftBottomAnchor, CuiPoint aRightTopAnchor, CuiPoint aLeftBottomOffset, CuiPoint aRightTopOffset, CuiColor aColor,
                string aText, string aName = "", int aFontSize = 14, TextAnchor aAlign = TextAnchor.UpperLeft
            ) {
                if (aLeftBottomAnchor == null || aRightTopAnchor == null || aLeftBottomOffset == null || aRightTopOffset == null || aColor == null) {
                    FPluginInstance.LogError($"Cui::AddLabel > One of the required parameters is null");
                    return string.Empty;
                }

                FPluginInstance.LogDebug("Added label to container");
                return FContainer.Add(
                    new CuiLabel
                    {
                        Text =
                        {
                            Text = aText ?? string.Empty,
                            FontSize = aFontSize,
                            Align = aAlign,
                            Color = aColor.ToString()
                        },
                        RectTransform =
                        {
                            AnchorMin = aLeftBottomAnchor.ToString(),
                            AnchorMax = aRightTopAnchor.ToString(),
                            OffsetMin = aLeftBottomOffset.ToString(),
                            OffsetMax = aRightTopOffset.ToString()
                        }
                    },
                    aParent,
                    string.IsNullOrEmpty(aName) ? null : aName
                );
            }

            /// <summary>
            /// Add a new button
            /// </summary>
            /// <param name="aParent">The parent object name</param>
            /// <param name="aLeftBottomAnchor">Left(x)-Bottom(y) relative position</param>
            /// <param name="aRightTopAnchor">Right(x)-Top(y) relative position</param>
            /// <param name="aButtonColor">Button background color</param>
            /// <param name="aTextColor">Text color</param>
            /// <param name="aText">Text to show</param>
            /// <param name="aCommand">OnClick event callback command</param>
            /// <param name="aClose">Panel to close</param>
            /// <param name="aName">The object's name</param>
            /// <param name="aFontSize">Font size</param>
            /// <param name="aAlign">Text alignment</param>
            /// <returns>New object name</returns>
            public string AddButton(
                string aParent, CuiPoint aLeftBottomAnchor, CuiPoint aRightTopAnchor, CuiColor aButtonColor, CuiColor aTextColor, string aText,
                string aCommand = "", string aClose = "", string aName = "", int aFontSize = 14, TextAnchor aAlign = TextAnchor.MiddleCenter
            ) => AddButton(
                    aParent, aLeftBottomAnchor, aRightTopAnchor, CuiPoint.Zero, CuiPoint.Zero, aButtonColor, aTextColor, aText, aCommand, aClose, aName,
                    aFontSize, aAlign
                );

            /// <summary>
            /// Add a new button
            /// </summary>
            /// <param name="aParent">The parent object name</param>
            /// <param name="aLeftBottomAnchor">Left(x)-Bottom(y) relative position</param>
            /// <param name="aRightTopAnchor">Right(x)-Top(y) relative position</param>
            /// <param name="aLeftBottomOffset">Left(x)-Bottom(y) relative offset</param>
            /// <param name="aRightTopOffset">Right(x)-Top(y) relative offset</param>
            /// <param name="aButtonColor">Button background color</param>
            /// <param name="aTextColor">Text color</param>
            /// <param name="aText">Text to show</param>
            /// <param name="aCommand">OnClick event callback command</param>
            /// <param name="aClose">Panel to close</param>
            /// <param name="aName">The object's name</param>
            /// <param name="aFontSize">Font size</param>
            /// <param name="aAlign">Text alignment</param>
            /// <returns>New object name</returns>
            public string AddButton(
                string aParent, CuiPoint aLeftBottomAnchor, CuiPoint aRightTopAnchor, CuiPoint aLeftBottomOffset, CuiPoint aRightTopOffset,
                CuiColor aButtonColor, CuiColor aTextColor, string aText, string aCommand = "", string aClose = "", string aName = "", int aFontSize = 14,
                TextAnchor aAlign = TextAnchor.MiddleCenter
            ) {
                if (
                    aLeftBottomAnchor == null || aRightTopAnchor == null || aLeftBottomOffset == null || aRightTopOffset == null || aButtonColor == null ||
                    aTextColor == null
                ) {
                    FPluginInstance.LogError($"Cui::AddButton > One of the required parameters is null");
                    return string.Empty;
                }

                FPluginInstance.LogDebug("Added button to container");
                return FContainer.Add(
                    new CuiButton
                    {
                        Button =
                        {
                            Command = aCommand ?? string.Empty,
                            Close = aClose ?? string.Empty,
                            Color = aButtonColor.ToString()
                        },
                        RectTransform =
                        {
                            AnchorMin = aLeftBottomAnchor.ToString(),
                            AnchorMax = aRightTopAnchor.ToString(),
                            OffsetMin = aLeftBottomOffset.ToString(),
                            OffsetMax = aRightTopOffset.ToString()
                        },
                        Text =
                        {
                            Text = aText ?? string.Empty,
                            FontSize = aFontSize,
                            Align = aAlign,
                            Color = aTextColor.ToString()
                        }
                    },
                    aParent,
                    string.IsNullOrEmpty(aName) ? null : aName
                );
            }

            /// <summary>
            /// Add a new input field
            /// </summary>
            /// <param name="aParent">The parent object name</param>
            /// <param name="aLeftBottomAnchor">Left(x)-Bottom(y) relative position</param>
            /// <param name="aRightTopAnchor">Right(x)-Top(y) relative position</param>
            /// <param name="aColor">Text color</param>
            /// <param name="aText">Text to show</param>
            /// <param name="aCharsLimit">Max character count</param>
            /// <param name="aCommand">OnChanged event callback command</param>
            /// <param name="aIndPassword">Indicates that this input should show password chars</param>
            /// <param name="aName">The object's name</param>
            /// <param name="aFontSize">Font size</param>
            /// <param name="aAlign">Text alignment</param>
            /// <returns>New object name</returns>
            public string AddInputField(
                string aParent, CuiPoint aLeftBottomAnchor, CuiPoint aRightTopAnchor, CuiColor aColor, string aText = "", int aCharsLimit = 100,
                string aCommand = "", bool aIndPassword = false, string aName = "", int aFontSize = 14, TextAnchor aAlign = TextAnchor.MiddleLeft
            ) => AddInputField(
                    aParent, aLeftBottomAnchor, aRightTopAnchor, CuiPoint.Zero, CuiPoint.Zero, aColor, aText, aCharsLimit, aCommand, aIndPassword, aName,
                    aFontSize, aAlign
                );

            /// <summary>
            /// Add a new input field
            /// </summary>
            /// <param name="aParent">The parent object name</param>
            /// <param name="aLeftBottomAnchor">Left(x)-Bottom(y) relative position</param>
            /// <param name="aRightTopAnchor">Right(x)-Top(y) relative position</param>
            /// <param name="aLeftBottomOffset">Left(x)-Bottom(y) relative offset</param>
            /// <param name="aRightTopOffset">Right(x)-Top(y) relative offset</param>
            /// <param name="fadeOut">Fade-out time</param>
            /// <param name="aColor">Text color</param>
            /// <param name="aText">Text to show</param>
            /// <param name="aCharsLimit">Max character count</param>
            /// <param name="aCommand">OnChanged event callback command</param>
            /// <param name="aIndPassword">Indicates that this input should show password chars</param>
            /// <param name="aName">The object's name</param>
            /// <param name="aFontSize">Font size</param>
            /// <returns>New object name</returns>
            public string AddInputField(
                string aParent, CuiPoint aLeftBottomAnchor, CuiPoint aRightTopAnchor, CuiPoint aLeftBottomOffset, CuiPoint aRightTopOffset, CuiColor aColor,
                string aText = "", int aCharsLimit = 100, string aCommand = "", bool aIndPassword = false, string aName = "", int aFontSize = 14,
                TextAnchor aAlign = TextAnchor.MiddleLeft
            ) {
                if (aLeftBottomAnchor == null || aRightTopAnchor == null || aLeftBottomOffset == null || aRightTopOffset == null || aColor == null) {
                    FPluginInstance.LogError($"Cui::AddInputField > One of the required parameters is null");
                    return string.Empty;
                }

                FPluginInstance.LogDebug("Added input field to container");
                return FContainer.Add(
                    new CuiInputField
                    {
                        InputField =
                        {
                            Text = aText ?? string.Empty,
                            FontSize = aFontSize,
                            Align = aAlign,
                            Color = aColor.ToString(),
                            CharsLimit = aCharsLimit,
                            Command = aCommand ?? string.Empty,
                            IsPassword = aIndPassword
                        },
                        RectTransform =
                        {
                            AnchorMin = aLeftBottomAnchor.ToString(),
                            AnchorMax = aRightTopAnchor.ToString(),
                            OffsetMin = aLeftBottomOffset.ToString(),
                            OffsetMax = aRightTopOffset.ToString()
                        }
                    },
                    aParent,
                    string.IsNullOrEmpty(aName) ? null : aName
                );
            }

            /// <summary>
            /// Add a new element
            /// </summary>
            /// <param name="aParent">The parent object name</param>
            /// <param name="aElement">The object itself</param>
            /// <param name="aName">The object's name</param>
            /// <returns>New object name</returns>
            public string AddElement(string aParent, CuiPanel aElement, string aName = "") =>
                FContainer.Add(aElement, aParent, string.IsNullOrEmpty(aName) ? null : aName);

            /// <summary>
            /// Add a new element
            /// </summary>
            /// <param name="aParent">The parent object name</param>
            /// <param name="aElement">The object itself</param>
            /// <param name="aName">The object's name</param>
            /// <returns>New object name</returns>
            public string AddElement(string aParent, CuiLabel aElement, string aName = "") =>
                FContainer.Add(aElement, aParent, string.IsNullOrEmpty(aName) ? null : aName);

            /// <summary>
            /// Add a new element
            /// </summary>
            /// <param name="aParent">The parent object name</param>
            /// <param name="aElement">The object itself</param>
            /// <param name="aName">The object's name</param>
            /// <returns>New object name</returns>
            public string AddElement(string aParent, CuiButton aElement, string aName = "") =>
                FContainer.Add(aElement, aParent, string.IsNullOrEmpty(aName) ? null : aName);

            /// <summary>
            /// Add a new element
            /// </summary>
            /// <param name="aParent">The parent object name</param>
            /// <param name="aElement">The object itself</param>
            /// <param name="aName">The object's name</param>
            /// <returns>New object name</returns>
            public string AddElement(string aParent, CuiInputField aElement, string aName = "") =>
                FContainer.Add(aElement, aParent, string.IsNullOrEmpty(aName) ? null : aName);

            /// <summary>
            /// Draw the UI to the player's client
            /// </summary>
            /// <returns></returns>
            public bool Draw() => CuiHelper.AddUi(FPlayer, CuiHelper.ToJson(FContainer, false));
        }
        #endregion GUI

        #region Utility methods
        /// <summary>
        /// Get a "page" of entities from a specified list
        /// </summary>
        /// <param name="aList">List of entities</param>
        /// <param name="aPage">Page number (Starting from 0)</param>
        /// <param name="aPageSize">Page size</param>
        /// <returns>List of entities</returns>
        private List<T> GetPage<T>(IList<T> aList, int aPage, int aPageSize) => aList.Skip(aPage * aPageSize).Take(aPageSize).ToList();

        /// <summary>
        /// Add a button to the tab menu
        /// </summary>
        /// <param name="aUIObj">Cui object</param>
        /// <param name="aParent">Name of the parent object</param>
        /// <param name="aCaption">Text to show</param>
        /// <param name="aCommand">Button to execute</param>
        /// <param name="aPos">Bounds of the button</param>
        /// <param name="aIndActive">To indicate whether or not the button is active</param>
        private void AddTabMenuBtn(ref Cui aUIObj, string aParent, string aCaption, string aCommand, int aPos, bool aIndActive)
        {
            Vector2 dimensions = new Vector2(0.096f, 0.75f);
            Vector2 offset = new Vector2(0.005f, 0.1f);
            CuiColor btnColor = (aIndActive ? CuiColor.ButtonInactive : CuiColor.Button);
            CuiPoint lbAnchor = new CuiPoint(((dimensions.x + offset.x) * aPos) + offset.x, offset.y);
            CuiPoint rtAnchor = new CuiPoint(lbAnchor.X + dimensions.x, offset.y + dimensions.y);
            aUIObj.AddButton(aParent, lbAnchor, rtAnchor, btnColor, CuiColor.TextAlt, aCaption, (aIndActive ? string.Empty : aCommand));
        }

        /// <summary>
        /// Add a set of user buttons to the parent object
        /// </summary>
        /// <param name="aUIObj">Cui object</param>
        /// <param name="aParent">Name of the parent object</param>
        /// <param name="aUserList">List of entities</param>
        /// <param name="aCommandFmt">Base format of the command to execute (Will be completed with the user ID</param>
        /// <param name="aPage">User list page</param>
        private void AddPlayerButtons<T>(ref Cui aUIObj, string aParent, ref List<T> aUserList, string aCommandFmt, int aPage)
        {
            List<T> userRange = GetPage(aUserList, aPage, CMaxPlayerButtons);
            Vector2 dimensions = new Vector2(0.194f, 0.06f);
            Vector2 offset = new Vector2(0.005f, 0.01f);
            int col = -1;
            int row = 0;
            float margin = 0.09f;
            List<string> addedNames = new List<string>();

            foreach (T user in userRange) {
                if (++col >= CMaxPlayerCols) {
                    row++;
                    col = 0;
                }

                float calcTop = (1f - margin) - (((dimensions.y + offset.y) * row) + offset.y);
                float calcLeft = ((dimensions.x + offset.x) * col) + offset.x;
                CuiPoint lbAnchor = new CuiPoint(calcLeft, calcTop - dimensions.y);
                CuiPoint rtAnchor = new CuiPoint(calcLeft + dimensions.x, calcTop);
                string btnText;
                string btnCommand;
                int suffix = 0;

                if (typeof(T) == typeof(BasePlayer)) {
                    BasePlayer player = user as BasePlayer;
                    btnText = EscapeString(player.displayName);
                    btnCommand = string.Format(aCommandFmt, player.UserIDString);

                    while (addedNames.FindIndex(item => btnText.Equals(item, StringComparison.OrdinalIgnoreCase)) >= 0) {
                        btnText = $"{EscapeString(player.displayName)} {++suffix}";
                    }
                } else {
                    ServerUsers.User player = user as ServerUsers.User;
                    string btnTextTemp = EscapeString(player.username);
                    btnCommand = string.Format(aCommandFmt, player.steamid);

                    if (string.IsNullOrEmpty(btnTextTemp) || CUnknownNameList.Contains(btnTextTemp.ToLower()))
                        btnTextTemp = player.steamid.ToString();

                    btnText = btnTextTemp;

                    while (addedNames.FindIndex(item => btnText.Equals(item, StringComparison.OrdinalIgnoreCase)) >= 0) {
                        btnText = $"{btnTextTemp} {++suffix}";
                    }
                }

                aUIObj.AddButton(aParent, lbAnchor, rtAnchor, CuiColor.Button, CuiColor.TextAlt, btnText, btnCommand, string.Empty, string.Empty, 16);
                addedNames.Add(btnText);
            }

            LogDebug("Added the player buttons to the container");
        }

        /// <summary>
        /// Get translated message for the specified key
        /// </summary>
        /// <param name="aKey">Message key</param>
        /// <param name="aPlayerId">Player ID</param>
        /// <param name="aArgs">Optional args</param>
        /// <returns></returns>
        private string GetMessage(string aKey, string aPlayerId, params object[] aArgs) => string.Format(lang.GetMessage(aKey, this, aPlayerId), aArgs);

        /// <summary>
        /// Log an error message to the logfile
        /// </summary>
        /// <param name="aMessage"></param>
        private void LogError(string aMessage) =>  LogToFile(string.Empty, $"[{DateTime.Now.ToString("hh:mm:ss")}] ERROR > {aMessage}", this);

        /// <summary>
        /// Log an informational message to the logfile
        /// </summary>
        /// <param name="aMessage"></param>
        private void LogInfo(string aMessage) => LogToFile(string.Empty, $"[{DateTime.Now.ToString("hh:mm:ss")}] INFO > {aMessage}", this);

        /// <summary>
        /// Log a debugging message to the logfile
        /// </summary>
        /// <param name="aMessage"></param>
        private void LogDebug(string aMessage)
        {
            if (CDebugEnabled)
                LogToFile(string.Empty, $"[{DateTime.Now.ToString("hh:mm:ss")}] DEBUG > {aMessage}", this);
        }

        /// <summary>
        /// Send a message to a specific player
        /// </summary>
        /// <param name="aPlayer">The player to send the message to</param>
        /// <param name="aMessage">The message to send</param>
        private void SendMessage(ref BasePlayer aPlayer, string aMessage) => rust.SendChatMessage(aPlayer, string.Empty, aMessage);

        /// <summary>
        /// Verify if a user has the specified permission
        /// </summary>
        /// <param name="aPlayer">The player</param>
        /// <param name="aPermission">Pass <see cref="string.Empty"/> to only verify <see cref="CPermUiShow"/></param>
        /// <param name="aIndReport">Indicates that issues should be reported</param>
        /// <returns></returns>
        private bool VerifyPermission(ref BasePlayer aPlayer, string aPermission, bool aIndReport = false)
        {
            bool result = permission.UserHasPermission(aPlayer.UserIDString, CPermUiShow);
            aPermission = aPermission ?? string.Empty; // We need to get rid of possible null values

            if (FConfigData.UsePermSystem && result && aPermission.Length > 0)
                result = permission.UserHasPermission(aPlayer.UserIDString, aPermission);

            if (aIndReport && !result) {
                SendMessage(ref aPlayer, GetMessage("Permission Error Text", aPlayer.UserIDString));
                LogError(GetMessage("Permission Error Log Text", aPlayer.UserIDString, aPlayer.displayName, aPermission));
            }

            return result;
        }

        /// <summary>
        /// Verify if a user has the specified permission
        /// </summary>
        /// <param name="aPlayerId">The player's ID</param>
        /// <param name="aPermission">Pass <see cref="string.Empty"/> to only verify <see cref="CPermUiShow"/></param>
        /// <returns></returns>
        private bool VerifyPermission(string aPlayerId, string aPermission)
        {
            BasePlayer player = BasePlayer.Find(aPlayerId);
            return VerifyPermission(ref player, aPermission);
        }

        /// <summary>
        /// Retrieve server users
        /// </summary>
        /// <param name="aIndFiltered">Indicates if the output should be filtered</param>
        /// <param name="aUserId">User ID for retrieving filter text</param>
        /// <param name="aIndOffline">Retrieve the list of sleepers (offline players)</param>
        /// <returns></returns>
        private List<BasePlayer> GetServerUserList(bool aIndFiltered, string aUserId, bool aIndOffline = false)
        {
            List<BasePlayer> result = new List<BasePlayer>();
            ulong userId = ulong.Parse(aUserId);

            if (aIndOffline) {
                Player.Sleepers.ForEach(user => {
                    ServerUsers.User servUser = ServerUsers.Get(user.userID);

                    if (servUser == null || servUser?.group != ServerUsers.UserGroup.Banned)
                        result.Add(user);
                });
            } else {
                Player.Players.ForEach(user => {
                    ServerUsers.User servUser = ServerUsers.Get(user.userID);

                    if (servUser == null || servUser?.group != ServerUsers.UserGroup.Banned)
                        result.Add(user);
                });
            }

            if (aIndFiltered && FUserBtnPageSearchInputText.ContainsKey(userId))
                result = result.Where(x =>
                        x.displayName.IndexOf(FUserBtnPageSearchInputText[userId], StringComparison.OrdinalIgnoreCase) >= 0 ||
                        x.UserIDString.IndexOf(FUserBtnPageSearchInputText[userId], StringComparison.OrdinalIgnoreCase) >= 0
                    ).ToList();

            LogDebug("Retrieved the server user list");
            result.Sort((a, b) => {
                int diff = string.Compare(a.displayName, b.displayName);

                if (diff == 0)
                    diff = a.userID.CompareTo(b.userID);

                return diff;
            });
            return result;
        }

        /// <summary>
        /// Retrieve server users
        /// </summary>
        /// <param name="aIndFiltered">Indicates if the output should be filtered</param>
        /// <param name="aUserId">User ID for retrieving filter text</param>
        /// <returns></returns>
        private List<ServerUsers.User> GetBannedUserList(bool aIndFiltered, string aUserId)
        {
            List<ServerUsers.User> result = ServerUsers.GetAll(ServerUsers.UserGroup.Banned).ToList();
            ulong userId = ulong.Parse(aUserId);

            if (aIndFiltered && FUserBtnPageSearchInputText.ContainsKey(userId))
                result = result.Where(x =>
                        x.username.IndexOf(FUserBtnPageSearchInputText[userId], StringComparison.OrdinalIgnoreCase) >= 0 ||
                        x.steamid.ToString().IndexOf(FUserBtnPageSearchInputText[userId], StringComparison.OrdinalIgnoreCase) >= 0
                    ).ToList();

            LogDebug("Retrieved the banned user list");
            result.Sort((a, b) => {
                int diff = string.Compare(a.username, b.username);

                if (diff == 0)
                    diff = a.steamid.CompareTo(b.steamid);

                return diff;
            });
            return result;
        }

        /// <summary>
        /// Retrieve the target player ID from the arguments and report success
        /// </summary>
        /// <param name="aArg">Argument object</param>
        /// <param name="aTarget">Player ID</param>
        /// <returns></returns>
        private bool GetTargetFromArg(string[] aArgs, out ulong aTarget)
        {
            aTarget = 0;
            return aArgs.Count() > 0 && ulong.TryParse(aArgs[0], out aTarget);
        }

        /// <summary>
        /// Retrieve the target player ID and amount from the arguments and report success
        /// </summary>
        /// <param name="aArg">Argument object</param>
        /// <param name="aTarget">Player ID</param>
        /// <param name="aAmount">Amount</param>
        /// <returns></returns>
        private bool GetTargetAmountFromArg(string[] aArgs, out ulong aTarget, out float aAmount)
        {
            aTarget = 0;
            aAmount = 0;
            return aArgs.Count() >= 2 && ulong.TryParse(aArgs[0], out aTarget) && float.TryParse(aArgs[1], out aAmount);
        }

        /// <summary>
        /// Check if the player has the VoiceMuted flag set
        /// </summary>
        /// <param name="aPlayer">The player</param>
        /// <returns></returns>
        private bool GetIsVoiceMuted(ref BasePlayer aPlayer) => aPlayer.HasPlayerFlag(BasePlayer.PlayerFlags.VoiceMuted);

        /// <summary>
        /// Check if the player has the ChatMute flag set
        /// </summary>
        /// <param name="aPlayer">The player</param>
        /// <returns></returns>
        private bool GetIsChatMuted(ref BasePlayer aPlayer)
        {
            bool isServerMuted = aPlayer.HasPlayerFlag(BasePlayer.PlayerFlags.ChatMute);

            if (BetterChatMute != null) {
                return isServerMuted || (bool)BetterChatMute.Call("API_IsMuted", aPlayer.IPlayer);
            } else {
                return isServerMuted;
            }
        }

        /// <summary>
        /// Check if the player has the freeze.frozen permission
        /// </summary>
        /// <param name="aPlayerId">The player's ID</param>
        /// <returns></returns>
        private bool GetIsFrozen(ulong aPlayerId) => permission.UserHasPermission(aPlayerId.ToString(), CPermFreezeFrozen);

        /// <summary>
        /// Send either a kick or a ban message to Discord via the DiscordMessages plugin
        /// </summary>
        /// <param name="aAdminName">The name of the admin</param>
        /// <param name="aAdminId">The ID of the admin</param>
        /// <param name="aTargetName">The name of the target player</param>
        /// <param name="aTargetId">The ID of the target player</param>
        /// <param name="aReason">The reason message</param>
        /// <param name="aIndIsBan">If this is true a ban message is sent, else a kick message is sent</param>
        private void SendDiscordKickBanMessage(string aAdminName, string aAdminId, string aTargetName, string aTargetId, string aReason, bool aIndIsBan)
        {
            if (DiscordMessages != null) {
                if (CUnknownNameList.Contains(aTargetName.ToLower()))
                    aTargetName = aTargetId;

                object fields = new[]
                {
                    new {
                        name = "Player",
                        value = $"[{aTargetName}](https://steamcommunity.com/profiles/{aTargetId})",
                        inline = true
                    },
                    new {
                        name = aIndIsBan ? "Banned by" : "Kicked by",
                        value = $"[{aAdminName}](https://steamcommunity.com/profiles/{aAdminId})",
                        inline = true
                    },
                    new {
                        name = "Reason",
                        value = aReason,
                        inline = false
                    }
                };
                DiscordMessages.Call(
                    "API_SendFancyMessage",
                    aIndIsBan ? FConfigData.BanMsgWebhookUrl : FConfigData.KickMsgWebhookUrl,
                    aIndIsBan ? "Player Ban" : "Player Kick",
                    3329330,
                    JsonConvert.SerializeObject(fields)
                );
            }
        }

        /// <summary>
        /// Custom internal version of Assert
        /// </summary>
        /// <param name="aEval"></param>
        /// <param name="aTrueMsg"></param>
        /// <param name="aFalseMsg"></param>
        /// <returns></returns>
        private string InternalAssert(bool aEval, string aTrueMsg = "True", string aFalseMsg = "False") => aEval ? aTrueMsg : aFalseMsg;

        /// <summary>
        /// Transform a string array into a printable string.
        /// </summary>
        /// <param name="aObj"></param>
        /// <returns></returns>
        private string StringArrToString(ref string[] aObj) => $"[ {string.Join(", ", aObj)} ]";

        /// <summary>
        /// Transform a dictionary of strings into a printable string.
        /// </summary>
        /// <param name="aObj"></param>
        /// <returns></returns>
        private string StringDictToString(ref Dictionary<string, string> aObj)
        {
            StringBuilder result = new StringBuilder("{\n");

            foreach (KeyValuePair<string, string> item in aObj) {
                result.Append($"'{item.Key}': '{item.Value}'\n");
            }

            result.Append("}");
            return result.ToString();
        }

        /// <summary>
        /// Escape strings to make them usable in the UI.
        /// </summary>
        /// <param name="aStr"></param>
        /// <returns></returns>
        private string EscapeString(string aStr) => aStr.Replace("\0", "")
            .Replace("\a", "")
            .Replace("\b", "")
            .Replace("\f", "")
            .Replace("\r", "")
            .Replace("\n", " ")
            .Replace("\t", " ")
            .Replace("\v", "")
            .Replace("\"", "\\\"")
            .Replace("/", "\\/")
            .Replace("\\", "\\\\");
        #endregion Utility methods

        #region Upgrade methods
        /// <summary>
        /// Upgrade the config to 1.3.10 if needed
        /// </summary>
        /// <returns></returns>
        private bool UpgradeTo1310()
        {
            bool result = false;
            Config.Load();

            if (Config["Use Permission System"] == null) {
                FConfigData.UsePermSystem = true;
                result = true;
            }

            // Remove legacy config items
            if (
                Config["Enable kick action"] != null || Config["Enable ban action"] != null || Config["Enable unban action"] != null ||
                Config["Enable kill action"] != null || Config["Enable inventory clear action"] != null || Config["Enable blueprint reset action"] != null ||
                Config["Enable metabolism reset action"] != null || Config["Enable hurt action"] != null || Config["Enable heal action"] != null ||
                Config["Enable voice mute action"] != null || Config["Enable chat mute action"] != null || Config["Enable perms action"] != null ||
                Config["Enable freeze action"] != null
            )
                result = true;

            Config.Clear();

            if (result)
                Config.WriteObject(FConfigData);

            return result;
        }

        /// <summary>
        /// Upgrade the config to 1.3.13 if needed
        /// </summary>
        /// <returns></returns>
        private bool UpgradeTo1313()
        {
            bool result = false;
            Config.Load();

            if (Config["Discord Webhook url for ban messages"] == null) {
                FConfigData.BanMsgWebhookUrl = string.Empty;
                result = true;
            }

            if (Config["Discord Webhook url for kick messages"] == null) {
                FConfigData.KickMsgWebhookUrl = string.Empty;
                result = true;
            }

            Config.Clear();

            if (result)
                Config.WriteObject(FConfigData);

            return result;
        }

        /// <summary>
        /// Upgrade the config to 1.5.6 if needed
        /// </summary>
        /// <returns></returns>
        private bool UpgradeTo156()
        {
            bool result = false;
            Dictionary<string, string> oldPerms = new Dictionary<string, string>() {
                { "playeradministration.show", CPermUiShow },
                { "playeradministration.kick", CPermKick },
                { "playeradministration.ban", CPermBan },
                { "playeradministration.kill", CPermKill },
                { "playeradministration.perms", CPermPerms },
                { "playeradministration.voicemute", CPermVoiceMute },
                { "playeradministration.chatmute", CPermChatMute },
                { "playeradministration.freeze", CPermFreeze },
                { "playeradministration.clearinventory", CPermClearInventory },
                { "playeradministration.resetblueprint", CPermResetBP },
                { "playeradministration.resetmetabolism", CPermResetMetabolism },
                { "playeradministration.recovermetabolism", CPermRecoverMetabolism },
                { "playeradministration.hurt", CPermHurt },
                { "playeradministration.heal", CPermHeal },
                { "playeradministration.teleport", CPermTeleport },
                { "playeradministration.spectate", CPermSpectate }
            };
            LogDebug($"Old Perms: {StringDictToString(ref oldPerms)}");

            foreach (KeyValuePair<string, string> item in oldPerms) {
                string[] groups = permission.GetPermissionGroups(item.Key);
                LogDebug($"Groups: {StringArrToString(ref groups)}");
                string[] users = permission.GetPermissionUsers(item.Key);
                LogDebug($"Users: {StringArrToString(ref users)}");

                if (groups.Count() <= 0 && users.Count() <= 0) {
                    LogDebug("Counts are zero");
                    continue;
                }

                result = true;

                foreach (string group in groups) {
                    permission.RevokeGroupPermission(group, item.Key);
                    permission.GrantGroupPermission(group, item.Value, this);
                    LogInfo($"Fixed group permission: {group} (OLD) {item.Key} -> (NEW) {item.Value}");
                }

                foreach (string user in users) {
                    string uid = user.Substring(0, user.IndexOf('('));
                    permission.RevokeUserPermission(uid, item.Key);
                    permission.GrantUserPermission(uid, item.Value, this);
                    LogInfo($"Fixed user permission: {user} (OLD) {item.Key} -> (NEW) {item.Value}");
                }
            }

            permission.SaveData();
            return result;
        }
        #endregion

        #region GUI build methods
        /// <summary>
        /// Build the tab nav-bar
        /// </summary>
        /// <param name="aUIObj">Cui object</param>
        /// <param name="aPageType">The active page type</param>
        private void BuildTabMenu(ref Cui aUIObj, UiPage aPageType)
        {
            // Add the panels and title label
            string headerPanel = aUIObj.AddElement(CMainPanelName, CTabHeaderPanel);
            string tabBtnPanel = aUIObj.AddElement(CMainPanelName, CTabTabBtnPanel);
            aUIObj.AddElement(headerPanel, CTabMenuHeaderLbl);
            aUIObj.AddElement(headerPanel, CTabMenuCloseBtn);
            // Add the tab menu buttons
            AddTabMenuBtn(
                ref aUIObj, tabBtnPanel, GetMessage("Main Tab Text", aUIObj.PlayerIdString), $"{CSwitchUiCmd} {CCmdArgMain}", 0,
                aPageType == UiPage.Main ? true : false
            );
            AddTabMenuBtn(
                ref aUIObj, tabBtnPanel, GetMessage("Online Player Tab Text", aUIObj.PlayerIdString), $"{CSwitchUiCmd} {CCmdArgPlayersOnline} 0", 1,
                aPageType == UiPage.PlayersOnline ? true : false
            );
            AddTabMenuBtn(
                ref aUIObj, tabBtnPanel, GetMessage("Offline Player Tab Text", aUIObj.PlayerIdString), $"{CSwitchUiCmd} {CCmdArgPlayersOffline} 0", 2,
                aPageType == UiPage.PlayersOffline ? true : false
            );
            AddTabMenuBtn(
                ref aUIObj, tabBtnPanel, GetMessage("Banned Player Tab Text", aUIObj.PlayerIdString), $"{CSwitchUiCmd} {CCmdArgPlayersBanned} 0", 3,
                aPageType == UiPage.PlayersBanned ? true : false
            );
            LogDebug("Built the tab menu");
        }

        /// <summary>
        /// Build the main-menu
        /// </summary>
        /// <param name="aUIObj">Cui object</param>
        private void BuildMainPage(ref Cui aUIObj)
        {
            // Add the panels and title
            string panel = aUIObj.AddElement(CMainPanelName, CMainPagePanel);
            aUIObj.AddElement(panel, CMainPageTitleLbl);
            // Add the ban by ID group
            aUIObj.AddLabel(
                panel, CMainPageLblBanByIdTitleLbAnchor, CMainPageLblBanByIdTitleRtAnchor, CuiColor.TextTitle,
                GetMessage("Ban By ID Title Text", aUIObj.PlayerIdString), string.Empty, 16, TextAnchor.MiddleLeft
            );
            aUIObj.AddLabel(
                panel, CMainPageLblBanByIdLbAnchor, CMainPageLblBanByIdRtAnchor, CuiColor.TextAlt,
                GetMessage("Ban By ID Label Text", aUIObj.PlayerIdString), string.Empty, 14, TextAnchor.MiddleLeft
            );
            string panelBanByIdGroup = aUIObj.AddElement(panel, CBanByIdGroupPanel);

            if (VerifyPermission(aUIObj.PlayerIdString, CPermBan)) {
                aUIObj.AddElement(panelBanByIdGroup, CBanByIdEdt);
                aUIObj.AddElement(panel, CBanByIdActiveBtn);
            } else {
                aUIObj.AddElement(panel, CBanByIdInactiveBtn);
            }

            LogDebug("Built the main page");
        }

        /// <summary>
        /// Build a page of user buttons
        /// </summary>
        /// <param name="aUIObj">Cui object</param>
        /// <param name="aPageType">The active page type</param>
        /// <param name="aPage">User list page</param>
        /// <param name="aIndFiltered">Indicates if the output should be filtered</param>
        private void BuildUserBtnPage(ref Cui aUIObj, UiPage aPageType, int aPage, bool aIndFiltered)
        {
            string npBtnCommandFmt;
            int userCount;

            string panel = aUIObj.AddElement(CMainPanelName, CMainPagePanel);
            aUIObj.AddLabel(
                panel, CUserBtnPageLblTitleLbAnchor, CUserBtnPageLblTitleRtAnchor, CuiColor.TextAlt,
                GetMessage("User Button Page Title Text", aUIObj.PlayerIdString), string.Empty, 18, TextAnchor.MiddleLeft
            );
            // Add search elements
            aUIObj.AddLabel(
                panel, CUserBtnPageLblSearchLbAnchor, CUserBtnPageLblSearchRtAnchor, CuiColor.TextAlt, GetMessage("Search Label Text", aUIObj.PlayerIdString),
                string.Empty, 16, TextAnchor.MiddleLeft
            );
            string panelSearchGroup = aUIObj.AddElement(panel, CUserBtnPageSearchInputPanel);
            aUIObj.AddInputField(
                panelSearchGroup, CUserBtnPageEdtSearchInputLbAnchor, CUserBtnPageEdtSearchInputRtAnchor, CuiColor.TextAlt,
                (FUserBtnPageSearchInputText.ContainsKey(aUIObj.PlayerId) ? FUserBtnPageSearchInputText[aUIObj.PlayerId] : string.Empty), 100,
                CUserBtnPageSearchInputTextCmd, false, string.Empty, 16
            );

            switch (aPageType) {
                case UiPage.PlayersOnline: {
                    aUIObj.AddButton(
                        panel, CUserBtnPageBtnSearchLbAnchor, CUserBtnPageBtnSearchRtAnchor, CuiColor.Button, CuiColor.TextAlt,
                        GetMessage("Go Button Text", aUIObj.PlayerIdString), $"{CSwitchUiCmd} {CCmdArgPlayersOnlineSearch} 0", string.Empty, string.Empty, 16
                    );
                    BuildUserButtons(ref aUIObj, panel, aPageType, ref aPage, out npBtnCommandFmt, out userCount, aIndFiltered);
                    break;
                }
                case UiPage.PlayersOffline: {
                    aUIObj.AddButton(
                        panel, CUserBtnPageBtnSearchLbAnchor, CUserBtnPageBtnSearchRtAnchor, CuiColor.Button, CuiColor.TextAlt,
                        GetMessage("Go Button Text", aUIObj.PlayerIdString), $"{CSwitchUiCmd} {CCmdArgPlayersOfflineSearch} 0", string.Empty, string.Empty, 16
                    );
                    BuildUserButtons(ref aUIObj, panel, aPageType, ref aPage, out npBtnCommandFmt, out userCount, aIndFiltered);
                    break;
                }
                default: {
                    aUIObj.AddButton(
                        panel, CUserBtnPageBtnSearchLbAnchor, CUserBtnPageBtnSearchRtAnchor, CuiColor.Button, CuiColor.TextAlt,
                        GetMessage("Go Button Text", aUIObj.PlayerIdString), $"{CSwitchUiCmd} {CCmdArgPlayersBannedSearch} 0", string.Empty, string.Empty, 16
                    );
                    BuildBannedUserButtons(ref aUIObj, panel, ref aPage, out npBtnCommandFmt, out userCount, aIndFiltered);
                    break;
                }
            }

            if (aPageType == UiPage.PlayersOnline || aPageType == UiPage.PlayersOffline) {
                BuildUserButtons(ref aUIObj, panel, aPageType, ref aPage, out npBtnCommandFmt, out userCount, aIndFiltered);
            } else {
                BuildBannedUserButtons(ref aUIObj, panel, ref aPage, out npBtnCommandFmt, out userCount, aIndFiltered);
            }

            // Decide whether or not to activate the "previous" button
            if (aPage == 0) {
                aUIObj.AddElement(panel, CUserBtnPagePreviousInactiveBtn);
            } else {
                aUIObj.AddButton(
                    panel, CUserBtnPageBtnPreviousLbAnchor, CUserBtnPageBtnPreviousRtAnchor, CuiColor.Button, CuiColor.TextAlt, "<<",
                    string.Format(npBtnCommandFmt, aPage - 1), string.Empty, string.Empty, 18
                );
            }

            // Decide whether or not to activate the "next" button
            if (userCount > CMaxPlayerButtons * (aPage + 1)) {
                aUIObj.AddButton(
                    panel, CUserBtnPageBtnNextLbAnchor, CUserBtnPageBtnNextRtAnchor, CuiColor.Button, CuiColor.TextAlt, ">>",
                    string.Format(npBtnCommandFmt, aPage + 1), string.Empty, string.Empty, 18
                );
            } else {
                aUIObj.AddElement(panel, CUserBtnPageNextInactiveBtn);
            }

            LogDebug("Built the user button page");
        }

        /// <summary>
        /// Build the current user buttons
        /// </summary>
        /// <param name="aUIObj">Cui object</param>
        /// <param name="aParent">The active page type</param>
        /// <param name="aPageType">The active page type</param>
        /// <param name="aPage">User list page</param>
        /// <param name="aBtnCommandFmt">Command format for the buttons</param>
        /// <param name="aUserCount">Total user count</param>
        /// <param name="aIndFiltered">Indicates if the output should be filtered</param>
        private void BuildUserButtons(
            ref Cui aUIObj, string aParent, UiPage aPageType, ref int aPage, out string aBtnCommandFmt, out int aUserCount, bool aIndFiltered
        ) {
            string commandFmt = $"{CSwitchUiCmd} {CCmdArgPlayerPage} {{0}}";
            List<BasePlayer> userList;

            if (aPageType == UiPage.PlayersOnline) {
                userList = GetServerUserList(aIndFiltered, aUIObj.PlayerIdString);
                aBtnCommandFmt = $"{CSwitchUiCmd} {(aIndFiltered ? CCmdArgPlayersOnlineSearch : CCmdArgPlayersOnline)} {{0}}";
            } else {
                userList = GetServerUserList(aIndFiltered, aUIObj.PlayerIdString, true);
                aBtnCommandFmt = $"{CSwitchUiCmd} {(aIndFiltered ? CCmdArgPlayersOfflineSearch : CCmdArgPlayersOffline)} {{0}}";
            }

            aUserCount = userList.Count;

            if ((aPage != 0) && (userList.Count <= CMaxPlayerButtons))
                aPage = 0; // Reset page to 0 if user count is lower or equal to max button count

            AddPlayerButtons(ref aUIObj, aParent, ref userList, commandFmt, aPage);
            LogDebug("Built the current page of user buttons");
        }

        /// <summary>
        /// Build the banned user buttons
        /// </summary>
        /// <param name="aUIObj">Cui object</param>
        /// <param name="aParent">The active page type</param>
        /// <param name="aPage">User list page</param>
        /// <param name="aBtnCommandFmt">Command format for the buttons</param>
        /// <param name="aUserCount">Total user count</param>
        /// <param name="aIndFiltered">Indicates if the output should be filtered</param>
        private void BuildBannedUserButtons(ref Cui aUIObj, string aParent, ref int aPage, out string aBtnCommandFmt, out int aUserCount, bool aIndFiltered)
        {
            string commandFmt = $"{CSwitchUiCmd} {CCmdArgPlayerPageBanned} {{0}}";
            List<ServerUsers.User> userList = GetBannedUserList(aIndFiltered, aUIObj.PlayerIdString);
            aBtnCommandFmt = $"{CSwitchUiCmd} {(aIndFiltered ? CCmdArgPlayersBannedSearch : CCmdArgPlayersBanned)} {{0}}";
            aUserCount = userList.Count;

            if ((aPage != 0) && (userList.Count <= CMaxPlayerButtons))
                aPage = 0; // Reset page to 0 if user count is lower or equal to max button count

            AddPlayerButtons(ref aUIObj, aParent, ref userList, commandFmt, aPage);
            LogDebug("Built the current page of banned user buttons");
        }

        /// <summary>
        /// Add the user information labels to the parent element
        /// </summary>
        /// <param name="aUIObj">Cui object</param>
        /// <param name="aParent">Parent panel name</param>
        /// <param name="aUiUserId">UI destination Player ID (SteamId64)</param>
        /// <param name="aPlayerId">Player ID (SteamId64)</param>
        /// <param name="aPlayer">Player who's information we need to display</param>
        private void AddUserPageInfoLabels(ref Cui aUIObj, string aParent, string aUiUserId, ulong aPlayerId, ref BasePlayer aPlayer)
        {
            string lastCheatStr = GetMessage("Never Label Text", aUiUserId);
            string authLevel = ServerUsers.Get(aPlayerId)?.group.ToString() ?? "None";

            // Pre-calc last admin cheat
            if (aPlayer.lastAdminCheatTime > 0f) {
                TimeSpan lastCheatSinceStart = new TimeSpan(0, 0, (int)(Time.realtimeSinceStartup - aPlayer.lastAdminCheatTime));
                lastCheatStr = $"{DateTime.UtcNow.Subtract(lastCheatSinceStart).ToString(@"yyyy\/MM\/dd HH:mm:ss")} UTC";
            }

            LogDebug("AddUserPageInfoLabels > Time since last admin cheat has been determined.");
            aUIObj.AddLabel(
                aParent, CUserPageLblIdLbAnchor, CUserPageLblIdRtAnchor, CuiColor.TextAlt,
                GetMessage("Id Label Format", aUiUserId, aPlayerId, (aPlayer.IsDeveloper ? GetMessage("Dev Label Text", aUiUserId) : string.Empty)),
                string.Empty, 14, TextAnchor.MiddleLeft
            );
            /*
            aUIObj.AddInputField(
                aParent, CUserPageLblIdLbAnchor, CUserPageLblIdRtAnchor, CuiColor.TextAlt,
                GetMessage("Id Label Format", aUiUserId, aPlayerId, (aPlayer.IsDeveloper ? GetMessage("Dev Label Text", aUiUserId) : string.Empty)),
                100, string.Empty, false, string.Empty, 14, TextAnchor.MiddleLeft
            );
            */
            aUIObj.AddLabel(
                aParent, CUserPageLblAuthLbAnchor, CUserPageLblAuthRtAnchor, CuiColor.TextAlt,
                GetMessage("Auth Level Label Format", aUiUserId, authLevel), string.Empty, 14, TextAnchor.MiddleLeft
            );
            aUIObj.AddLabel(
                aParent, CUserPageLblConnectLbAnchor, CUserPageLblConnectRtAnchor, CuiColor.TextAlt, GetMessage(
                    "Connection Label Format", aUiUserId,
                    (aPlayer.IsConnected ? GetMessage("Connected Label Text", aUiUserId) : GetMessage("Disconnected Label Text", aUiUserId))
                ), string.Empty, 14, TextAnchor.MiddleLeft
            );
            aUIObj.AddLabel(
                aParent, CUserPageLblSleepLbAnchor, CUserPageLblSleepRtAnchor, CuiColor.TextAlt, GetMessage(
                    "Status Label Format", aUiUserId,
                    (aPlayer.IsSleeping() ? GetMessage("Sleeping Label Text", aUiUserId) : GetMessage("Awake Label Text", aUiUserId)),
                    (aPlayer.IsAlive() ? GetMessage("Alive Label Text", aUiUserId) : GetMessage("Dead Label Text", aUiUserId))
                ), string.Empty, 14, TextAnchor.MiddleLeft
            );

            LogDebug("AddUserPageInfoLabels > Generic info has been added.");

            if (VerifyPermission(aUiUserId, CPermDetailInfo)) {
                aUIObj.AddLabel(
                    aParent, CUserPageLblFlagLbAnchor, CUserPageLblFlagRtAnchor, CuiColor.TextAlt, GetMessage(
                        "Flags Label Format", aUiUserId, (aPlayer.IsFlying ? GetMessage("Flying Label Text", aUiUserId) : string.Empty),
                        (aPlayer.isMounted ? GetMessage("Mounted Label Text", aUiUserId) : string.Empty)
                    ), string.Empty, 14, TextAnchor.MiddleLeft
                );
                aUIObj.AddLabel(
                    aParent, CUserPageLblPosLbAnchor, CUserPageLblPosRtAnchor, CuiColor.TextAlt,
                    GetMessage("Position Label Format", aUiUserId, aPlayer.ServerPosition), string.Empty, 14, TextAnchor.MiddleLeft
                );
                aUIObj.AddLabel(
                    aParent, CUserPageLblRotLbAnchor, CUserPageLblRotRtAnchor, CuiColor.TextAlt,
                    GetMessage("Rotation Label Format", aUiUserId, aPlayer.GetNetworkRotation()), string.Empty, 14, TextAnchor.MiddleLeft
                );
                aUIObj.AddLabel(
                    aParent, CUserPageLblAdminCheatLbAnchor, CUserPageLblAdminCheatRtAnchor, CuiColor.TextAlt,
                    GetMessage("Last Admin Cheat Label Format", aUiUserId, lastCheatStr), string.Empty, 14, TextAnchor.MiddleLeft
                );
                aUIObj.AddLabel(
                    aParent, CUserPageLblIdleLbAnchor, CUserPageLblIdleRtAnchor, CuiColor.TextAlt,
                    GetMessage("Idle Time Label Format", aUiUserId, Math.Round(aPlayer.IdleTime, 2)), string.Empty, 14, TextAnchor.MiddleLeft
                );

                if (Economics != null) {
                    aUIObj.AddLabel(
                        aParent, CUserPageLblBalanceLbAnchor, CUserPageLblBalanceRtAnchor, CuiColor.TextAlt,
                        GetMessage("Economics Balance Label Format", aUiUserId, Math.Round((double)Economics.Call("Balance", aPlayerId), 2)), string.Empty, 14,
                        TextAnchor.MiddleLeft
                    );
                    LogDebug("AddUserPageInfoLabels > Economics info has been added.");
                }

                if (ServerRewards != null) {
                    aUIObj.AddLabel(
                        aParent, CUserPageLblRewardPointsLbAnchor, CUserPageLblRewardPointsRtAnchor, CuiColor.TextAlt,
                        GetMessage("ServerRewards Points Label Format", aUiUserId, (int)(ServerRewards.Call("CheckPoints", aPlayerId) ?? 0)), string.Empty, 14,
                        TextAnchor.MiddleLeft
                    );
                    LogDebug("AddUserPageInfoLabels > ServerRewards info has been added.");
                }

                aUIObj.AddLabel(
                    aParent, CUserPageLblHealthLbAnchor, CUserPageLblHealthRtAnchor, CuiColor.TextAlt, GetMessage("Health Label Format", aUiUserId, aPlayer.health),
                    string.Empty, 14, TextAnchor.MiddleLeft
                );
                aUIObj.AddLabel(
                    aParent, CUserPageLblCalLbAnchor, CUserPageLblCalRtAnchor, CuiColor.TextAlt,
                    GetMessage("Calories Label Format", aUiUserId, aPlayer.metabolism?.calories?.value), string.Empty, 14, TextAnchor.MiddleLeft
                );
                aUIObj.AddLabel(
                    aParent, CUserPageLblHydraLbAnchor, CUserPageLblHydraRtAnchor, CuiColor.TextAlt,
                    GetMessage("Hydration Label Format", aUiUserId, aPlayer.metabolism?.hydration?.value), string.Empty, 14, TextAnchor.MiddleLeft
                );
                aUIObj.AddLabel(
                    aParent, CUserPageLblTempLbAnchor, CUserPageLblTempRtAnchor, CuiColor.TextAlt,
                    GetMessage("Temp Label Format", aUiUserId, aPlayer.metabolism?.temperature?.value), string.Empty, 14, TextAnchor.MiddleLeft
                );
                aUIObj.AddLabel(
                    aParent, CUserPageLblWetLbAnchor, CUserPageLblWetRtAnchor, CuiColor.TextAlt,
                    GetMessage("Wetness Label Format", aUiUserId, aPlayer.metabolism?.wetness?.value), string.Empty, 14, TextAnchor.MiddleLeft
                );
                aUIObj.AddLabel(
                    aParent, CUserPageLblComfortLbAnchor, CUserPageLblComfortRtAnchor, CuiColor.TextAlt,
                    GetMessage("Comfort Label Format", aUiUserId, aPlayer.metabolism?.comfort?.value), string.Empty, 14, TextAnchor.MiddleLeft
                );
                aUIObj.AddLabel(
                    aParent, CUserPageLblBleedLbAnchor, CUserPageLblBleedRtAnchor, CuiColor.TextAlt,
                    GetMessage("Bleeding Label Format", aUiUserId, aPlayer.metabolism?.bleeding?.value), string.Empty, 14, TextAnchor.MiddleLeft
                );
                aUIObj.AddLabel(
                    aParent, CUserPageLblRads1LbAnchor, CUserPageLblRads1RtAnchor, CuiColor.TextAlt,
                    GetMessage("Radiation Label Format", aUiUserId, aPlayer.metabolism?.radiation_poison?.value), string.Empty, 14, TextAnchor.MiddleLeft
                );
                aUIObj.AddLabel(
                    aParent, CUserPageLblRads2LbAnchor, CUserPageLblRads2RtAnchor, CuiColor.TextAlt,
                    GetMessage("Radiation Protection Label Format", aUiUserId, aPlayer.RadiationProtection()), string.Empty, 14, TextAnchor.MiddleLeft
                );
                LogDebug("AddUserPageInfoLabels > Player statistics info has been added.");
            }
        }

        /// <summary>
        /// Add the first row of actions to the parent element
        /// </summary>
        /// <param name="aUIObj">Cui object</param>
        /// <param name="aParent">Parent panel name</param>
        /// <param name="aUiUserId">UI destination Player ID (SteamId64)</param>
        /// <param name="aPlayerId">Player ID (SteamId64)</param>
        /// <param name="aPlayer">Player who's information we need to display</param>
        private void AddUserPageFirstActionRow(ref Cui aUIObj, string aParent, string aUiUserId, ulong aPlayerId, ref BasePlayer aPlayer)
        {
            if (VerifyPermission(aUiUserId, CPermBan)) {
                aUIObj.AddButton(
                    aParent, CUserPageBtnBanLbAnchor, CUserPageBtnBanRtAnchor, CuiColor.ButtonDanger, CuiColor.TextAlt,
                    GetMessage("Ban Button Text", aUiUserId), $"{CBanUserCmd} {aPlayerId}"
                );
            } else {
                aUIObj.AddButton(
                    aParent, CUserPageBtnBanLbAnchor, CUserPageBtnBanRtAnchor, CuiColor.ButtonInactive, CuiColor.Text, GetMessage("Ban Button Text", aUiUserId)
                );
            }

            if (VerifyPermission(aUiUserId, CPermKick) && aPlayer.IsConnected) {
                aUIObj.AddButton(
                    aParent, CUserPageBtnKickLbAnchor, CUserPageBtnKickRtAnchor, CuiColor.ButtonDanger, CuiColor.TextAlt,
                    GetMessage("Kick Button Text", aUiUserId), $"{CKickUserCmd} {aPlayerId}"
                );
            } else {
                aUIObj.AddButton(
                    aParent, CUserPageBtnKickLbAnchor, CUserPageBtnKickRtAnchor, CuiColor.ButtonInactive, CuiColor.Text,
                    GetMessage("Kick Button Text", aUiUserId)
                );
            }

            aUIObj.AddLabel(
                aParent, CUserPageLblReasonLbAnchor, CUserPageLblReasonRtAnchor, CuiColor.TextAlt, GetMessage("Reason Input Label Text", aUiUserId),
                string.Empty, 14, TextAnchor.MiddleLeft
            );
            string panelReasonGroup = aUIObj.AddPanel(aParent, CUserPagePanelReasonLbAnchor, CUserPagePanelReasonRtAnchor, false, CuiColor.BackgroundDark);
            aUIObj.AddInputField(
                panelReasonGroup, CUserPageEdtReasonLbAnchor, CUserPageEdtReasonRtAnchor, CuiColor.TextAlt, string.Empty, 24, CUserPageReasonInputTextCmd
            );
        }

        /// <summary>
        /// Add the second row of actions to the parent element
        /// </summary>
        /// <param name="aUIObj">Cui object</param>
        /// <param name="aParent">Parent panel name</param>
        /// <param name="aUiUserId">UI destination Player ID (SteamId64)</param>
        /// <param name="aPlayerId">Player ID (SteamId64)</param>
        /// <param name="aPlayer">Player who's information we need to display</param>
        private void AddUserPageSecondActionRow(ref Cui aUIObj, string aParent, string aUiUserId, ulong aPlayerId, ref BasePlayer aPlayer)
        {
            bool playerConnected = aPlayer.IsConnected;

            if (VerifyPermission(aUiUserId, CPermVoiceMute) && playerConnected) {
                if (GetIsVoiceMuted(ref aPlayer)) {
                    aUIObj.AddButton(
                        aParent, CUserPageBtnVUnmuteLbAnchor, CUserPageBtnVUnmuteRtAnchor, CuiColor.ButtonSuccess, CuiColor.TextAlt,
                        GetMessage("Voice Unmute Button Text", aUiUserId), $"{CVoiceUnmuteUserCmd} {aPlayerId}"
                    );
                    aUIObj.AddButton(
                        aParent, CUserPageBtnVMuteLbAnchor, CUserPageBtnVMuteRtAnchor, CuiColor.ButtonInactive, CuiColor.Text,
                        GetMessage("Voice Mute Button Text", aUiUserId)
                    );
                } else {
                    aUIObj.AddButton(
                        aParent, CUserPageBtnVUnmuteLbAnchor, CUserPageBtnVUnmuteRtAnchor, CuiColor.ButtonInactive, CuiColor.Text,
                        GetMessage("Voice Unmute Button Text", aUiUserId)
                    );
                    aUIObj.AddButton(
                        aParent, CUserPageBtnVMuteLbAnchor, CUserPageBtnVMuteRtAnchor, CuiColor.ButtonDanger, CuiColor.TextAlt,
                        GetMessage("Voice Mute Button Text", aUiUserId), $"{CVoiceMuteUserCmd} {aPlayerId}"
                    );
                }
            } else {
                aUIObj.AddButton(
                    aParent, CUserPageBtnVUnmuteLbAnchor, CUserPageBtnVUnmuteRtAnchor, CuiColor.ButtonInactive, CuiColor.Text,
                    GetMessage("Voice Unmute Button Text", aUiUserId)
                );
                aUIObj.AddButton(
                    aParent, CUserPageBtnVMuteLbAnchor, CUserPageBtnVMuteRtAnchor, CuiColor.ButtonInactive, CuiColor.Text,
                    GetMessage("Voice Mute Button Text", aUiUserId)
                );
            }
        }

        /// <summary>
        /// Add the third row of actions to the parent element
        /// </summary>
        /// <param name="aUIObj">Cui object</param>
        /// <param name="aParent">Parent panel name</param>
        /// <param name="aUiUserId">UI destination Player ID (SteamId64)</param>
        /// <param name="aPlayerId">Player ID (SteamId64)</param>
        /// <param name="aPlayer">Player who's information we need to display</param>
        private void AddUserPageThirdActionRow(ref Cui aUIObj, string aParent, string aUiUserId, ulong aPlayerId, ref BasePlayer aPlayer)
        {
            bool playerConnected = aPlayer.IsConnected;

            if (VerifyPermission(aUiUserId, CPermChatMute) && playerConnected) {
                if (GetIsChatMuted(ref aPlayer)) {
                    aUIObj.AddButton(
                        aParent, CUserPageBtnCUnmuteLbAnchor, CUserPageBtnCUnmuteRtAnchor, CuiColor.ButtonSuccess, CuiColor.TextAlt,
                        GetMessage("Chat Unmute Button Text", aUiUserId), $"{CChatUnmuteUserCmd} {aPlayerId}"
                    );
                    aUIObj.AddButton(
                        aParent, CUserPageBtnCMuteLbAnchor, CUserPageBtnCMuteRtAnchor, CuiColor.ButtonInactive, CuiColor.Text,
                        GetMessage("Chat Mute Button Text", aUiUserId)
                    );

                    if (BetterChatMute != null) {
                        aUIObj.AddButton(
                            aParent, CUserPageBtnCMuteFifteenLbAnchor, CUserPageBtnCMuteFifteenRtAnchor, CuiColor.ButtonInactive, CuiColor.Text,
                            GetMessage("Chat Mute Button Text 15", aUiUserId)
                        );
                        aUIObj.AddButton(
                            aParent, CUserPageBtnCMuteThirtyLbAnchor, CUserPageBtnCMuteThirtyRtAnchor, CuiColor.ButtonInactive, CuiColor.Text,
                            GetMessage("Chat Mute Button Text 30", aUiUserId)
                        );
                        aUIObj.AddButton(
                            aParent, CUserPageBtnCMuteSixtyLbAnchor, CUserPageBtnCMuteSixtyRtAnchor, CuiColor.ButtonInactive, CuiColor.Text,
                            GetMessage("Chat Mute Button Text 60", aUiUserId)
                        );
                    }
                } else {
                    aUIObj.AddButton(
                        aParent, CUserPageBtnCUnmuteLbAnchor, CUserPageBtnCUnmuteRtAnchor, CuiColor.ButtonInactive, CuiColor.Text,
                        GetMessage("Chat Unmute Button Text", aUiUserId)
                    );
                    aUIObj.AddButton(
                        aParent, CUserPageBtnCMuteLbAnchor, CUserPageBtnCMuteRtAnchor, CuiColor.ButtonDanger, CuiColor.TextAlt,
                        GetMessage("Chat Mute Button Text", aUiUserId), $"{CChatMuteUserCmd} {aPlayerId} 0"
                    );

                    if (BetterChatMute != null) {
                        aUIObj.AddButton(
                            aParent, CUserPageBtnCMuteFifteenLbAnchor, CUserPageBtnCMuteFifteenRtAnchor, CuiColor.ButtonDanger, CuiColor.TextAlt,
                            GetMessage("Chat Mute Button Text 15", aUiUserId), $"{CChatMuteUserCmd} {aPlayerId} 15"
                        );
                        aUIObj.AddButton(
                            aParent, CUserPageBtnCMuteThirtyLbAnchor, CUserPageBtnCMuteThirtyRtAnchor, CuiColor.ButtonDanger, CuiColor.TextAlt,
                            GetMessage("Chat Mute Button Text 30", aUiUserId), $"{CChatMuteUserCmd} {aPlayerId} 30"
                        );
                        aUIObj.AddButton(
                            aParent, CUserPageBtnCMuteSixtyLbAnchor, CUserPageBtnCMuteSixtyRtAnchor, CuiColor.ButtonDanger, CuiColor.TextAlt,
                            GetMessage("Chat Mute Button Text 60", aUiUserId), $"{CChatMuteUserCmd} {aPlayerId} 60"
                        );
                    }
                }
            } else {
                aUIObj.AddButton(
                    aParent, CUserPageBtnCUnmuteLbAnchor, CUserPageBtnCUnmuteRtAnchor, CuiColor.ButtonInactive, CuiColor.Text,
                    GetMessage("Chat Unmute Button Text", aUiUserId)
                );
                aUIObj.AddButton(
                    aParent, CUserPageBtnCMuteLbAnchor, CUserPageBtnCMuteRtAnchor, CuiColor.ButtonInactive, CuiColor.Text,
                    GetMessage("Chat Mute Button Text", aUiUserId)
                );
            }
        }

        /// <summary>
        /// Add the third row of actions to the parent element
        /// </summary>
        /// <param name="aUIObj">Cui object</param>
        /// <param name="aParent">Parent panel name</param>
        /// <param name="aUiUserId">UI destination Player ID (SteamId64)</param>
        /// <param name="aPlayerId">Player ID (SteamId64)</param>
        /// <param name="aPlayer">Player who's information we need to display</param>
        private void AddUserPageFourthActionRow(ref Cui aUIObj, string aParent, string aUiUserId, ulong aPlayerId, ref BasePlayer aPlayer)
        {
            if (Freeze == null) {
                aUIObj.AddButton(
                    aParent, CUserPageBtnFreezeLbAnchor, CUserPageBtnFreezeRtAnchor, CuiColor.ButtonInactive, CuiColor.Text,
                    GetMessage("Freeze Not Installed Button Text", aUiUserId)
                );
            } else if (VerifyPermission(aUiUserId, CPermFreeze) && aPlayer.IsConnected) {
                if (GetIsFrozen(aPlayerId)) {
                    aUIObj.AddButton(
                        aParent, CUserPageBtnUnFreezeLbAnchor, CUserPageBtnUnFreezeRtAnchor, CuiColor.ButtonSuccess, CuiColor.TextAlt,
                        GetMessage("UnFreeze Button Text", aUiUserId), $"{CUnreezeCmd} {aPlayerId}"
                    );
                    aUIObj.AddButton(
                        aParent, CUserPageBtnFreezeLbAnchor, CUserPageBtnFreezeRtAnchor, CuiColor.ButtonInactive, CuiColor.Text,
                        GetMessage("Freeze Button Text", aUiUserId)
                    );
                } else {
                    aUIObj.AddButton(
                        aParent, CUserPageBtnUnFreezeLbAnchor, CUserPageBtnUnFreezeRtAnchor, CuiColor.ButtonInactive, CuiColor.Text,
                        GetMessage("UnFreeze Button Text", aUiUserId)
                    );
                    aUIObj.AddButton(
                        aParent, CUserPageBtnFreezeLbAnchor, CUserPageBtnFreezeRtAnchor, CuiColor.ButtonDanger, CuiColor.TextAlt,
                        GetMessage("Freeze Button Text", aUiUserId), $"{CFreezeCmd} {aPlayerId}"
                    );
                }
            } else {
                aUIObj.AddButton(
                    aParent, CUserPageBtnUnFreezeLbAnchor, CUserPageBtnUnFreezeRtAnchor, CuiColor.ButtonInactive, CuiColor.Text,
                    GetMessage("UnFreeze Button Text", aUiUserId)
                );
                aUIObj.AddButton(
                    aParent, CUserPageBtnFreezeLbAnchor, CUserPageBtnFreezeRtAnchor, CuiColor.ButtonInactive, CuiColor.Text,
                    GetMessage("Freeze Button Text", aUiUserId)
                );
            }
        }

        /// <summary>
        /// Add the fourth row of actions to the parent element
        /// </summary>
        /// <param name="aUIObj">Cui object</param>
        /// <param name="aParent">Parent panel name</param>
        /// <param name="aUiUserId">UI destination Player ID (SteamId64)</param>
        /// <param name="aPlayerId">Player ID (SteamId64)</param>
        private void AddUserPageFifthActionRow(ref Cui aUIObj, string aParent, string aUiUserId, ulong aPlayerId)
        {
            if (VerifyPermission(aUiUserId, CPermClearInventory)) {
                aUIObj.AddButton(
                    aParent, CUserPageBtnClearInventoryLbAnchor, CUserPageBtnClearInventoryRtAnchor, CuiColor.ButtonWarning, CuiColor.TextAlt,
                    GetMessage("Clear Inventory Button Text", aUiUserId), $"{CClearUserInventoryCmd} {aPlayerId}"
                );
            } else {
                aUIObj.AddButton(
                    aParent, CUserPageBtnClearInventoryLbAnchor, CUserPageBtnClearInventoryRtAnchor, CuiColor.ButtonInactive, CuiColor.Text,
                    GetMessage("Clear Inventory Button Text", aUiUserId)
                );
            }

            if (VerifyPermission(aUiUserId, CPermResetBP)) {
                aUIObj.AddButton(
                    aParent, CUserPageBtnResetBPLbAnchor, CUserPageBtnResetBPRtAnchor, CuiColor.ButtonWarning, CuiColor.TextAlt,
                    GetMessage("Reset Blueprints Button Text", aUiUserId), $"{CResetUserBPCmd} {aPlayerId}"
                );
            } else {
                aUIObj.AddButton(
                    aParent, CUserPageBtnResetBPLbAnchor, CUserPageBtnResetBPRtAnchor, CuiColor.ButtonInactive, CuiColor.Text,
                    GetMessage("Reset Blueprints Button Text", aUiUserId)
                );
            }

            if (VerifyPermission(aUiUserId, CPermResetMetabolism)) {
                aUIObj.AddButton(
                    aParent, CUserPageBtnResetMetabolismLbAnchor, CUserPageBtnResetMetabolismRtAnchor, CuiColor.ButtonWarning, CuiColor.TextAlt,
                    GetMessage("Reset Metabolism Button Text", aUiUserId), $"{CResetUserMetabolismCmd} {aPlayerId}"
                );
            } else {
                aUIObj.AddButton(
                    aParent, CUserPageBtnResetMetabolismLbAnchor, CUserPageBtnResetMetabolismRtAnchor, CuiColor.ButtonInactive, CuiColor.Text,
                    GetMessage("Reset Metabolism Button Text", aUiUserId)
                );
            }

            if (VerifyPermission(aUiUserId, CPermRecoverMetabolism)) {
                aUIObj.AddButton(
                    aParent, CUserPageBtnRecoverMetabolismLbAnchor, CUserPageBtnRecoverMetabolismRtAnchor, CuiColor.ButtonWarning, CuiColor.TextAlt,
                    GetMessage("Recover Metabolism Button Text", aUiUserId), $"{CRecoverUserMetabolismCmd} {aPlayerId}"
                );
            } else {
                aUIObj.AddButton(
                    aParent, CUserPageBtnRecoverMetabolismLbAnchor, CUserPageBtnRecoverMetabolismRtAnchor, CuiColor.ButtonInactive, CuiColor.Text,
                    GetMessage("Recover Metabolism Button Text", aUiUserId)
                );
            }
        }

        /// <summary>
        /// Add the fifth row of actions to the parent element
        /// </summary>
        /// <param name="aUIObj">Cui object</param>
        /// <param name="aParent">Parent panel name</param>
        /// <param name="aUiUserId">UI destination Player ID (SteamId64)</param>
        /// <param name="aPlayerId">Player ID (SteamId64)</param>
        private void AddUserPageSixthActionRow(ref Cui aUIObj, string aParent, string aUiUserId, ulong aPlayerId)
        {
            if (VerifyPermission(aUiUserId, CPermTeleport)) {
                aUIObj.AddButton(
                    aParent, CUserPageBtnTeleportToLbAnchor, CUserPageBtnTeleportToRtAnchor, CuiColor.ButtonSuccess, CuiColor.TextAlt,
                    GetMessage("Teleport To Player Button Text", aUiUserId), $"{CTeleportToUserCmd} {aPlayerId}"
                );
                aUIObj.AddButton(
                    aParent, CUserPageBtnTeleportLbAnchor, CUserPageBtnTeleportRtAnchor, CuiColor.ButtonSuccess, CuiColor.TextAlt,
                    GetMessage("Teleport Player Button Text", aUiUserId), $"{CTeleportUserCmd} {aPlayerId}"
                );
            } else {
                aUIObj.AddButton(
                    aParent, CUserPageBtnTeleportToLbAnchor, CUserPageBtnTeleportToRtAnchor, CuiColor.ButtonInactive, CuiColor.Text,
                    GetMessage("Teleport To Player Button Text", aUiUserId)
                );
                aUIObj.AddButton(
                    aParent, CUserPageBtnTeleportLbAnchor, CUserPageBtnTeleportRtAnchor, CuiColor.ButtonInactive, CuiColor.Text,
                    GetMessage("Teleport Player Button Text", aUiUserId)
                );
            }

            if (VerifyPermission(aUiUserId, CPermSpectate)) {
                aUIObj.AddButton(
                    aParent, CUserPageBtnSpectateLbAnchor, CUserPageBtnSpectateRtAnchor, CuiColor.ButtonSuccess, CuiColor.TextAlt,
                    GetMessage("Spectate Player Button Text", aUiUserId), $"{CSpectateUserCmd} {aPlayerId}"
                );
            } else {
                aUIObj.AddButton(
                    aParent, CUserPageBtnSpectateLbAnchor, CUserPageBtnSpectateRtAnchor, CuiColor.ButtonInactive, CuiColor.Text,
                    GetMessage("Spectate Player Button Text", aUiUserId)
                );
            }
        }

        /// <summary>
        /// Add the sixth row of actions to the parent element
        /// </summary>
        /// <param name="aUIObj">Cui object</param>
        /// <param name="aParent">Parent panel name</param>
        /// <param name="aUiUserId">UI destination Player ID (SteamId64)</param>
        /// <param name="aPlayerId">Player ID (SteamId64)</param>
        private void AddUserPageSeventhActionRow(ref Cui aUIObj, string aParent, string aUiUserId, ulong aPlayerId)
        {
            if (PermissionsManager == null) {
                aUIObj.AddButton(
                    aParent, CUserPageBtnPermsLbAnchor, CUserPageBtnPermsRtAnchor, CuiColor.ButtonInactive, CuiColor.Text,
                    GetMessage("Perms Not Installed Button Text", aUiUserId)
                );
            } else if (VerifyPermission(aUiUserId, CPermPerms)) {
                aUIObj.AddButton(
                    aParent, CUserPageBtnPermsLbAnchor, CUserPageBtnPermsRtAnchor, CuiColor.ButtonSuccess, CuiColor.TextAlt,
                    GetMessage("Perms Button Text", aUiUserId), $"{CPermsCmd} {aPlayerId}"
                );
            } else {
                aUIObj.AddButton(
                    aParent, CUserPageBtnPermsLbAnchor, CUserPageBtnPermsRtAnchor, CuiColor.ButtonInactive, CuiColor.Text,
                    GetMessage("Perms Button Text", aUiUserId)
                );
            }
        }

        /// <summary>
        /// Add the eleventh row of actions to the parent element
        /// </summary>
        /// <param name="aUIObj">Cui object</param>
        /// <param name="aParent">Parent panel name</param>
        /// <param name="aUiUserId">UI destination Player ID (SteamId64)</param>
        /// <param name="aPlayerId">Player ID (SteamId64)</param>
        private void AddUserPageEleventhActionRow(ref Cui aUIObj, string aParent, string aUiUserId, ulong aPlayerId)
        {
            if (VerifyPermission(aUiUserId, CPermHurt)) {
                aUIObj.AddButton(
                    aParent, CUserPageBtnHurt25LbAnchor, CUserPageBtnHurt25RtAnchor, CuiColor.ButtonDanger, CuiColor.TextAlt,
                    GetMessage("Hurt 25 Button Text", aUiUserId), $"{CHurtUserCmd} {aPlayerId} 25"
                );
                aUIObj.AddButton(
                    aParent, CUserPageBtnHurt50LbAnchor, CUserPageBtnHurt50RtAnchor, CuiColor.ButtonDanger, CuiColor.TextAlt,
                    GetMessage("Hurt 50 Button Text", aUiUserId), $"{CHurtUserCmd} {aPlayerId} 50"
                );
                aUIObj.AddButton(
                    aParent, CUserPageBtnHurt75LbAnchor, CUserPageBtnHurt75RtAnchor, CuiColor.ButtonDanger, CuiColor.TextAlt,
                    GetMessage("Hurt 75 Button Text", aUiUserId), $"{CHurtUserCmd} {aPlayerId} 75"
                );
                aUIObj.AddButton(
                    aParent, CUserPageBtnHurt100LbAnchor, CUserPageBtnHurt100RtAnchor, CuiColor.ButtonDanger, CuiColor.TextAlt,
                    GetMessage("Hurt 100 Button Text", aUiUserId), $"{CHurtUserCmd} {aPlayerId} 100"
                );
            } else {
                aUIObj.AddButton(
                    aParent, CUserPageBtnHurt25LbAnchor, CUserPageBtnHurt25RtAnchor, CuiColor.ButtonInactive, CuiColor.Text,
                    GetMessage("Hurt 25 Button Text", aUiUserId)
                );
                aUIObj.AddButton(
                    aParent, CUserPageBtnHurt50LbAnchor, CUserPageBtnHurt50RtAnchor, CuiColor.ButtonInactive, CuiColor.Text,
                    GetMessage("Hurt 50 Button Text", aUiUserId)
                );
                aUIObj.AddButton(
                    aParent, CUserPageBtnHurt75LbAnchor, CUserPageBtnHurt75RtAnchor, CuiColor.ButtonInactive, CuiColor.Text,
                    GetMessage("Hurt 75 Button Text", aUiUserId)
                );
                aUIObj.AddButton(
                    aParent, CUserPageBtnHurt100LbAnchor, CUserPageBtnHurt100RtAnchor, CuiColor.ButtonInactive, CuiColor.Text,
                    GetMessage("Hurt 100 Button Text", aUiUserId)
                );
            }

            if (VerifyPermission(aUiUserId, CPermKill)) {
                aUIObj.AddButton(
                    aParent, CUserPageBtnKillLbAnchor, CUserPageBtnKillRtAnchor, CuiColor.ButtonDanger, CuiColor.TextAlt,
                    GetMessage("Kill Button Text", aUiUserId), $"{CKillUserCmd} {aPlayerId}"
                );
            } else {
                aUIObj.AddButton(
                    aParent, CUserPageBtnKillLbAnchor, CUserPageBtnKillRtAnchor, CuiColor.ButtonInactive, CuiColor.Text,
                    GetMessage("Kill Button Text", aUiUserId)
                );
            }
        }

        /// <summary>
        /// Add the twelfth row of actions to the parent element
        /// </summary>
        /// <param name="aUIObj">Cui object</param>
        /// <param name="aParent">Parent panel name</param>
        /// <param name="aUiUserId">UI destination Player ID (SteamId64)</param>
        /// <param name="aPlayerId">Player ID (SteamId64)</param>
        private void AddUserPageTwelfthActionRow(ref Cui aUIObj, string aParent, string aUiUserId, ulong aPlayerId)
        {
            if (VerifyPermission(aUiUserId, CPermHeal)) {
                aUIObj.AddButton(
                    aParent, CUserPageBtnHeal25LbAnchor, CUserPageBtnHeal25RtAnchor, CuiColor.ButtonSuccess, CuiColor.TextAlt,
                    GetMessage("Heal 25 Button Text", aUiUserId), $"{CHealUserCmd} {aPlayerId} 25"
                );
                aUIObj.AddButton(
                    aParent, CUserPageBtnHeal50LbAnchor, CUserPageBtnHeal50RtAnchor, CuiColor.ButtonSuccess, CuiColor.TextAlt,
                    GetMessage("Heal 50 Button Text", aUiUserId), $"{CHealUserCmd} {aPlayerId} 50"
                );
                aUIObj.AddButton(
                    aParent, CUserPageBtnHeal75LbAnchor, CUserPageBtnHeal75RtAnchor, CuiColor.ButtonSuccess, CuiColor.TextAlt,
                    GetMessage("Heal 75 Button Text", aUiUserId), $"{CHealUserCmd} {aPlayerId} 75"
                );
                aUIObj.AddButton(
                    aParent, CUserPageBtnHeal100LbAnchor, CUserPageBtnHeal100RtAnchor, CuiColor.ButtonSuccess, CuiColor.TextAlt,
                    GetMessage("Heal 100 Button Text", aUiUserId), $"{CHealUserCmd} {aPlayerId} 100"
                );
                aUIObj.AddButton(
                    aParent, CUserPageBtnHealWoundsLbAnchor, CUserPageBtnHealWoundsRtAnchor, CuiColor.ButtonSuccess, CuiColor.TextAlt,
                    GetMessage("Heal Wounds Button Text", aUiUserId), $"{CHealUserCmd} {aPlayerId} 0"
                );
            } else {
                aUIObj.AddButton(
                    aParent, CUserPageBtnHeal25LbAnchor, CUserPageBtnHeal25RtAnchor, CuiColor.ButtonInactive, CuiColor.Text,
                    GetMessage("Heal 25 Button Text", aUiUserId)
                );
                aUIObj.AddButton(
                    aParent, CUserPageBtnHeal50LbAnchor, CUserPageBtnHeal50RtAnchor, CuiColor.ButtonInactive, CuiColor.Text,
                    GetMessage("Heal 50 Button Text", aUiUserId)
                );
                aUIObj.AddButton(
                    aParent, CUserPageBtnHeal75LbAnchor, CUserPageBtnHeal75RtAnchor, CuiColor.ButtonInactive, CuiColor.Text,
                    GetMessage("Heal 75 Button Text", aUiUserId)
                );
                aUIObj.AddButton(
                    aParent, CUserPageBtnHeal100LbAnchor, CUserPageBtnHeal100RtAnchor, CuiColor.ButtonInactive, CuiColor.Text,
                    GetMessage("Heal 100 Button Text", aUiUserId)
                );
                aUIObj.AddButton(
                    aParent, CUserPageBtnHealWoundsLbAnchor, CUserPageBtnHealWoundsRtAnchor, CuiColor.ButtonInactive, CuiColor.Text,
                    GetMessage("Heal Wounds Button Text", aUiUserId)
                );
            }
        }

        /// <summary>
        /// Build the user information and administration page
        /// This kind of method will always be complex, so ignore metrics about it, please. :)
        /// </summary>
        /// <param name="aUIObj">Cui object</param>
        /// <param name="aPageType">The active page type</param>
        /// <param name="aPlayerId">Player ID (SteamId64)</param>
        private void BuildUserPage(ref Cui aUIObj, UiPage aPageType, ulong aPlayerId)
        {
            // Add panels
            string panel = aUIObj.AddPanel(CMainPanelName, CMainPanelLbAnchor, CMainPanelRtAnchor, false, CuiColor.Background);
            string infoPanel = aUIObj.AddPanel(panel, CUserPageInfoPanelLbAnchor, CUserPageInfoPanelRtAnchor, false, CuiColor.BackgroundMedium);
            string actionPanel = aUIObj.AddPanel(panel, CUserPageActionPanelLbAnchor, CUserPageActionPanelRtAnchor, false, CuiColor.BackgroundMedium);
            LogDebug("BuildUserPage > Panels have been added.");

            // Add title labels
            aUIObj.AddLabel(
                infoPanel, CUserPageLblinfoTitleLbAnchor, CUserPageLblinfoTitleRtAnchor, CuiColor.TextTitle,
                GetMessage("Player Info Label Text", aUIObj.PlayerIdString), string.Empty, 14, TextAnchor.MiddleLeft
            );
            aUIObj.AddLabel(
                actionPanel, CUserPageLblActionTitleLbAnchor, CUserPageLblActionTitleRtAnchor, CuiColor.TextTitle,
                GetMessage("Player Actions Label Text", aUIObj.PlayerIdString), string.Empty, 14, TextAnchor.MiddleLeft
            );
            LogDebug("BuildUserPage > Title lables have been added.");

            if (aPageType == UiPage.PlayerPage) {
                BasePlayer player = BasePlayer.FindByID(aPlayerId) ?? BasePlayer.FindSleeping(aPlayerId);
                LogDebug("BuildUserPage > player = null? " + InternalAssert(player == null));

                aUIObj.AddLabel(
                    panel, CMainLblTitleLbAnchor, CMainLblTitleRtAnchor, CuiColor.TextAlt,
                    GetMessage("User Page Title Format", aUIObj.PlayerIdString, EscapeString(player.displayName), string.Empty), string.Empty, 18,
                    TextAnchor.MiddleLeft
                );
                // Add user info labels
                AddUserPageInfoLabels(ref aUIObj, infoPanel, aUIObj.PlayerIdString, aPlayerId, ref player);

                // --- Build player action panel
                // Ban, Kick
                AddUserPageFirstActionRow(ref aUIObj, actionPanel, aUIObj.PlayerIdString, aPlayerId, ref player);
                // Unmute voice, Mute voice
                AddUserPageSecondActionRow(ref aUIObj, actionPanel, aUIObj.PlayerIdString, aPlayerId, ref player);
                // Unmute chat, Mute chat (And timed ones if BetterChat is available)
                AddUserPageThirdActionRow(ref aUIObj, actionPanel, aUIObj.PlayerIdString, aPlayerId, ref player);
                // Unfreeze, Freeze
                AddUserPageFourthActionRow(ref aUIObj, actionPanel, aUIObj.PlayerIdString, aPlayerId, ref player);
                // Clear inventory, Reset BP, Reset metabolism
                AddUserPageFifthActionRow(ref aUIObj, actionPanel, aUIObj.PlayerIdString, aPlayerId);
                // Teleport to, Teleport, Spectate
                AddUserPageSixthActionRow(ref aUIObj, actionPanel, aUIObj.PlayerIdString, aPlayerId);
                // Perms
                AddUserPageSeventhActionRow(ref aUIObj, actionPanel, aUIObj.PlayerIdString, aPlayerId);
                // Hurt 25, Hurt 50, Hurt 75, Hurt 100, Kill
                AddUserPageEleventhActionRow(ref aUIObj, actionPanel, aUIObj.PlayerIdString, aPlayerId);
                // Heal 25, Heal 50, Heal 75, Heal 100, Heal wounds
                AddUserPageTwelfthActionRow(ref aUIObj, actionPanel, aUIObj.PlayerIdString, aPlayerId);
            } else {
                ServerUsers.User serverUser = ServerUsers.Get(aPlayerId);
                aUIObj.AddLabel(
                    panel, CMainLblTitleLbAnchor, CMainLblTitleRtAnchor, CuiColor.TextAlt,
                    GetMessage("User Page Title Format", aUIObj.PlayerIdString, serverUser.username, GetMessage("Banned Label Text", aUIObj.PlayerIdString)),
                    string.Empty, 18, TextAnchor.MiddleLeft
                );
                // Add user info labels
                aUIObj.AddLabel(
                    infoPanel, CUserPageLblIdLbAnchor, CUserPageLblIdRtAnchor, CuiColor.TextAlt,
                    GetMessage("Id Label Format", aUIObj.PlayerIdString, aPlayerId, string.Empty), string.Empty, 14, TextAnchor.MiddleLeft
                );

                // --- Build player action panel
                if (VerifyPermission(aUIObj.PlayerIdString, CPermBan)) {
                    aUIObj.AddButton(
                        actionPanel, CUserPageBtnBanLbAnchor, CUserPageBtnBanRtAnchor, CuiColor.Button, CuiColor.TextAlt,
                        GetMessage("Unban Button Text", aUIObj.PlayerIdString), $"{CUnbanUserCmd} {aPlayerId}"
                    );
                } else {
                    aUIObj.AddButton(
                        actionPanel, CUserPageBtnBanLbAnchor, CUserPageBtnBanRtAnchor, CuiColor.ButtonInactive, CuiColor.Text,
                        GetMessage("Unban Button Text", aUIObj.PlayerIdString)
                    );
                }
            }

            LogDebug("Built user information page");
        }

        /// <summary>
        /// Initiate the building of the UI page to show
        /// </summary>
        /// <param name="aPlayer">UI destination player</param>
        /// <param name="aPageType">Type of the page</param>
        /// <param name="aArg">Argument</param>
        /// <param name="aIndFiltered">Indicates if the output should be filtered</param>
        private void BuildUI(BasePlayer aPlayer, UiPage aPageType, string aArg = "", bool aIndFiltered = false)
        {
            // Initiate the new UI and panel
            Cui newUiLib = new Cui(aPlayer);
            newUiLib.AddElement(CBasePanelName, CMainPanel, CMainPanelName);
            BuildTabMenu(ref newUiLib, aPageType);

            switch (aPageType) {
                case UiPage.Main: {
                    BuildMainPage(ref newUiLib);
                    break;
                }
                case UiPage.PlayersOnline:
                case UiPage.PlayersOffline:
                case UiPage.PlayersBanned: {
                    int page = 0;

                    if (!string.IsNullOrEmpty(aArg))
                        int.TryParse(aArg, out page);

                    BuildUserBtnPage(ref newUiLib, aPageType, page, aIndFiltered);
                    break;
                }
                case UiPage.PlayerPage:
                case UiPage.PlayerPageBanned: {
                    ulong playerId = aPlayer.userID;

                    if (!string.IsNullOrEmpty(aArg))
                        ulong.TryParse(aArg, out playerId);

                    BuildUserPage(ref newUiLib, aPageType, playerId);
                    break;
                }
            }

            // Cleanup any old/active UI and draw the new one
            CuiHelper.DestroyUi(aPlayer, CMainPanelName);
            newUiLib.Draw();
        }
        #endregion GUI build methods

        #region Config
        /// <summary>
        /// The config type class
        /// </summary>
        private class ConfigData
        {
            [DefaultValue(true)]
            [JsonProperty("Use Permission System", DefaultValueHandling = DefaultValueHandling.Populate)]
            public bool UsePermSystem { get; set; }
            [DefaultValue("")]
            [JsonProperty("Discord Webhook url for ban messages", DefaultValueHandling = DefaultValueHandling.Populate)]
            public string BanMsgWebhookUrl { get; set; }
            [DefaultValue("")]
            [JsonProperty("Discord Webhook url for kick messages", DefaultValueHandling = DefaultValueHandling.Populate)]
            public string KickMsgWebhookUrl { get; set; }
        }
        #endregion

        #region Constants
        private static readonly bool CDebugEnabled = true;
        private const int CMaxPlayerCols = 5;
        private const int CMaxPlayerRows = 12;
        private const int CMaxPlayerButtons = CMaxPlayerCols * CMaxPlayerRows;
        private const string CBasePanelName = "PAdm_BasePanel";
        private const string CMainPanelName = "PAdm_MainPanel";
        private static readonly List<string> CUnknownNameList = new List<string> { "unnamed", "unknown" };

        #region Local commands
        private const string CPadminCmd = "padmin";
        private const string CCloseUiCmd = "playeradministration.closeui";
        private const string CSwitchUiCmd = "playeradministration.switchui";
        private const string CKickUserCmd = "playeradministration.kickuser";
        private const string CBanUserCmd = "playeradministration.banuser";
        private const string CMainPageBanByIdCmd = "playeradministration.mainpagebanbyid";
        private const string CUnbanUserCmd = "playeradministration.unbanuser";
        private const string CPermsCmd = "playeradministration.perms";
        private const string CVoiceMuteUserCmd = "playeradministration.vmuteuser";
        private const string CVoiceUnmuteUserCmd = "playeradministration.vunmuteuser";
        private const string CChatMuteUserCmd = "playeradministration.cmuteuser";
        private const string CChatUnmuteUserCmd = "playeradministration.cunmuteuser";
        private const string CFreezeCmd = "playeradministration.freeze";
        private const string CUnreezeCmd = "playeradministration.unfreeze";
        private const string CClearUserInventoryCmd = "playeradministration.clearuserinventory";
        private const string CResetUserBPCmd = "playeradministration.resetuserblueprints";
        private const string CResetUserMetabolismCmd = "playeradministration.resetusermetabolism";
        private const string CRecoverUserMetabolismCmd = "playeradministration.recoverusermetabolism";
        private const string CHurtUserCmd = "playeradministration.hurtuser";
        private const string CKillUserCmd = "playeradministration.killuser";
        private const string CHealUserCmd = "playeradministration.healuser";
        private const string CTeleportToUserCmd = "playeradministration.tptouser";
        private const string CTeleportUserCmd = "playeradministration.tpuser";
        private const string CSpectateUserCmd = "playeradministration.spectateuser";
        private const string CMainPageBanIdInputTextCmd = "playeradministration.mainpagebanidinputtext";
        private const string CUserBtnPageSearchInputTextCmd = "playeradministration.userbtnpagesearchinputtext";
        private const string CUserPageReasonInputTextCmd = "playeradministration.userpagereasoninputtext";
        #endregion Local commands

        #region Foreign commands
        private const string CPermsPermsCmd = "perms player";
        private const string CFreezeFreezeCmd = "freeze";
        private const string CFreezeUnfreezeCmd = "unfreeze";
        #endregion Foreign commands

        #region Local Command Static Arguments
        private const string CCmdArgMain = "main";
        private const string CCmdArgPlayersOnline = "playersonline";
        private const string CCmdArgPlayersOnlineSearch = "playersonlinesearch";
        private const string CCmdArgPlayersOffline = "playersoffline";
        private const string CCmdArgPlayersOfflineSearch = "playersofflinesearch";
        private const string CCmdArgPlayersBanned = "playersbanned";
        private const string CCmdArgPlayersBannedSearch = "playersbannedsearch";
        private const string CCmdArgPlayerPage = "playerpage";
        private const string CCmdArgPlayerPageBanned = "playerpagebanned";
        #endregion Local Command Static Arguments

        #region Local permissions
        private const string CPermUiShow = "playeradministration.access.show";
        private const string CPermKick = "playeradministration.access.kick";
        private const string CPermBan = "playeradministration.access.ban";
        private const string CPermKill = "playeradministration.access.kill";
        private const string CPermPerms = "playeradministration.access.perms";
        private const string CPermVoiceMute = "playeradministration.access.voicemute";
        private const string CPermChatMute = "playeradministration.access.chatmute";
        private const string CPermFreeze = "playeradministration.access.allowfreeze";
        private const string CPermClearInventory = "playeradministration.access.clearinventory";
        private const string CPermResetBP = "playeradministration.access.resetblueprint";
        private const string CPermResetMetabolism = "playeradministration.access.resetmetabolism";
        private const string CPermRecoverMetabolism = "playeradministration.access.recovermetabolism";
        private const string CPermHurt = "playeradministration.access.hurt";
        private const string CPermHeal = "playeradministration.access.heal";
        private const string CPermTeleport = "playeradministration.access.teleport";
        private const string CPermSpectate = "playeradministration.access.spectate";
        private const string CPermDetailInfo = "playeradministration.access.detailedinfo";
        private const string CPermProtectBan = "playeradministration.protect.ban";
        private const string CPermProtectHurt = "playeradministration.protect.hurt";
        private const string CPermProtectKick = "playeradministration.protect.kick";
        private const string CPermProtectKill = "playeradministration.protect.kill";
        private const string CPermProtectReset = "playeradministration.protect.reset";
        #endregion Local permissions

        #region Foreign permissions
        private const string CPermFreezeFrozen = "freeze.frozen";
        #endregion Foreign permissions

        /* Define layout */
        #region Main bounds
        private static readonly CuiPoint CMainLbAnchor = new CuiPoint(0.03f, 0.15f);
        private static readonly CuiPoint CMainRtAnchor = new CuiPoint(0.97f, 0.97f);
        private static readonly CuiPoint CMainMenuHeaderContainerLbAnchor = new CuiPoint(0.005f, 0.937f);
        private static readonly CuiPoint CMainMenuHeaderContainerRtAnchor = new CuiPoint(0.995f, 0.99f);
        private static readonly CuiPoint CMainMenuTabBtnContainerLbAnchor = new CuiPoint(0.005f, 0.867f);
        private static readonly CuiPoint CMainMenuTabBtnContainerRtAnchor = new CuiPoint(0.995f, 0.927f);
        private static readonly CuiPoint CMainMenuHeaderLblLbAnchor = new CuiPoint(0f, 0f);
        private static readonly CuiPoint CMainMenuHeaderLblRtAnchor = new CuiPoint(1f, 1f);
        private static readonly CuiPoint CMainMenuCloseBtnLbAnchor = new CuiPoint(0.965f, 0f);
        private static readonly CuiPoint CMainMenuCloseBtnRtAnchor = new CuiPoint(1f, 1f);
        private static readonly CuiPoint CMainPanelLbAnchor = new CuiPoint(0.005f, 0.01f);
        private static readonly CuiPoint CMainPanelRtAnchor = new CuiPoint(0.995f, 0.857f);
        private static readonly CuiPoint CMainLblTitleLbAnchor = new CuiPoint(0.005f, 0.93f);
        private static readonly CuiPoint CMainLblTitleRtAnchor = new CuiPoint(0.995f, 0.99f);
        #endregion Main bounds

        #region Main page bounds
        private static readonly CuiPoint CMainPageLblBanByIdTitleLbAnchor = new CuiPoint(0.005f, 0.84f);
        private static readonly CuiPoint CMainPageLblBanByIdTitleRtAnchor = new CuiPoint(0.995f, 0.89f);
        private static readonly CuiPoint CMainPageLblBanByIdLbAnchor = new CuiPoint(0.005f, 0.76f);
        private static readonly CuiPoint CMainPageLblBanByIdRtAnchor = new CuiPoint(0.05f, 0.81f);
        private static readonly CuiPoint CMainPagePanelBanByIdLbAnchor = new CuiPoint(0.055f, 0.76f);
        private static readonly CuiPoint CMainPagePanelBanByIdRtAnchor = new CuiPoint(0.305f, 0.81f);
        private static readonly CuiPoint CMainPageEdtBanByIdLbAnchor = new CuiPoint(0.005f, 0f);
        private static readonly CuiPoint CMainPageEdtBanByIdRtAnchor = new CuiPoint(0.995f, 1f);
        private static readonly CuiPoint CMainPageBtnBanByIdLbAnchor = new CuiPoint(0.315f, 0.76f);
        private static readonly CuiPoint CMainPageBtnBanByIdRtAnchor = new CuiPoint(0.365f, 0.81f);
        #endregion Main page bounds

        #region User button page bounds
        private static readonly CuiPoint CUserBtnPageLblTitleLbAnchor = new CuiPoint(0.005f, 0.93f);
        private static readonly CuiPoint CUserBtnPageLblTitleRtAnchor = new CuiPoint(0.495f, 0.99f);
        private static readonly CuiPoint CUserBtnPageLblSearchLbAnchor = new CuiPoint(0.52f, 0.93f);
        private static readonly CuiPoint CUserBtnPageLblSearchRtAnchor = new CuiPoint(0.565f, 0.99f);
        private static readonly CuiPoint CUserBtnPagePanelSearchInputLbAnchor = new CuiPoint(0.57f, 0.94f);
        private static readonly CuiPoint CUserBtnPagePanelSearchInputRtAnchor = new CuiPoint(0.945f, 0.99f);
        private static readonly CuiPoint CUserBtnPageEdtSearchInputLbAnchor = new CuiPoint(0.005f, 0f);
        private static readonly CuiPoint CUserBtnPageEdtSearchInputRtAnchor = new CuiPoint(0.995f, 1f);
        private static readonly CuiPoint CUserBtnPageBtnSearchLbAnchor = new CuiPoint(0.95f, 0.94f);
        private static readonly CuiPoint CUserBtnPageBtnSearchRtAnchor = new CuiPoint(0.995f, 0.99f);
        private static readonly CuiPoint CUserBtnPageBtnPreviousLbAnchor = new CuiPoint(0.005f, 0.01f);
        private static readonly CuiPoint CUserBtnPageBtnPreviousRtAnchor = new CuiPoint(0.035f, 0.06f);
        private static readonly CuiPoint CUserBtnPageBtnNextLbAnchor = new CuiPoint(0.96f, 0.01f);
        private static readonly CuiPoint CUserBtnPageBtnNextRtAnchor = new CuiPoint(0.995f, 0.06f);
        #endregion User button page bounds

        #region User page panel bounds
        private static readonly CuiPoint CUserPageInfoPanelLbAnchor = new CuiPoint(0.005f, 0.01f);
        private static readonly CuiPoint CUserPageInfoPanelRtAnchor = new CuiPoint(0.28f, 0.92f);
        private static readonly CuiPoint CUserPageActionPanelLbAnchor = new CuiPoint(0.285f, 0.01f);
        private static readonly CuiPoint CUserPageActionPanelRtAnchor = new CuiPoint(0.995f, 0.92f);
        #region User page title label bounds
        private static readonly CuiPoint CUserPageLblinfoTitleLbAnchor = new CuiPoint(0.025f, 0.94f);
        private static readonly CuiPoint CUserPageLblinfoTitleRtAnchor = new CuiPoint(0.975f, 0.99f);
        private static readonly CuiPoint CUserPageLblActionTitleLbAnchor = new CuiPoint(0.01f, 0.94f);
        private static readonly CuiPoint CUserPageLblActionTitleRtAnchor = new CuiPoint(0.99f, 0.99f);
        #endregion User page title label bounds
        #region User page info label bounds
        // Top part
        private static readonly CuiPoint CUserPageLblIdLbAnchor = new CuiPoint(0.025f, 0.88f);
        private static readonly CuiPoint CUserPageLblIdRtAnchor = new CuiPoint(0.975f, 0.92f);
        private static readonly CuiPoint CUserPageLblAuthLbAnchor = new CuiPoint(0.025f, 0.835f);
        private static readonly CuiPoint CUserPageLblAuthRtAnchor = new CuiPoint(0.975f, 0.875f);
        private static readonly CuiPoint CUserPageLblConnectLbAnchor = new CuiPoint(0.025f, 0.79f);
        private static readonly CuiPoint CUserPageLblConnectRtAnchor = new CuiPoint(0.975f, 0.83f);
        private static readonly CuiPoint CUserPageLblSleepLbAnchor = new CuiPoint(0.025f, 0.745f);
        private static readonly CuiPoint CUserPageLblSleepRtAnchor = new CuiPoint(0.975f, 0.785f);
        private static readonly CuiPoint CUserPageLblFlagLbAnchor = new CuiPoint(0.025f, 0.70f);
        private static readonly CuiPoint CUserPageLblFlagRtAnchor = new CuiPoint(0.975f, 0.74f);
        private static readonly CuiPoint CUserPageLblPosLbAnchor = new CuiPoint(0.025f, 0.655f);
        private static readonly CuiPoint CUserPageLblPosRtAnchor = new CuiPoint(0.975f, 0.695f);
        private static readonly CuiPoint CUserPageLblRotLbAnchor = new CuiPoint(0.025f, 0.61f);
        private static readonly CuiPoint CUserPageLblRotRtAnchor = new CuiPoint(0.975f, 0.65f);
        private static readonly CuiPoint CUserPageLblAdminCheatLbAnchor = new CuiPoint(0.025f, 0.555f);
        private static readonly CuiPoint CUserPageLblAdminCheatRtAnchor = new CuiPoint(0.975f, 0.605f);
        private static readonly CuiPoint CUserPageLblIdleLbAnchor = new CuiPoint(0.025f, 0.51f);
        private static readonly CuiPoint CUserPageLblIdleRtAnchor = new CuiPoint(0.975f, 0.55f);
        private static readonly CuiPoint CUserPageLblBalanceLbAnchor = new CuiPoint(0.025f, 0.465f);
        private static readonly CuiPoint CUserPageLblBalanceRtAnchor = new CuiPoint(0.975f, 0.505f);
        private static readonly CuiPoint CUserPageLblRewardPointsLbAnchor = new CuiPoint(0.025f, 0.42f);
        private static readonly CuiPoint CUserPageLblRewardPointsRtAnchor = new CuiPoint(0.975f, 0.46f);
        // Bottom part
        private static readonly CuiPoint CUserPageLblHealthLbAnchor = new CuiPoint(0.025f, 0.195f);
        private static readonly CuiPoint CUserPageLblHealthRtAnchor = new CuiPoint(0.975f, 0.235f);
        private static readonly CuiPoint CUserPageLblCalLbAnchor = new CuiPoint(0.025f, 0.145f);
        private static readonly CuiPoint CUserPageLblCalRtAnchor = new CuiPoint(0.5f, 0.19f);
        private static readonly CuiPoint CUserPageLblHydraLbAnchor = new CuiPoint(0.5f, 0.145f);
        private static readonly CuiPoint CUserPageLblHydraRtAnchor = new CuiPoint(0.975f, 0.19f);
        private static readonly CuiPoint CUserPageLblTempLbAnchor = new CuiPoint(0.025f, 0.10f);
        private static readonly CuiPoint CUserPageLblTempRtAnchor = new CuiPoint(0.5f, 0.14f);
        private static readonly CuiPoint CUserPageLblWetLbAnchor = new CuiPoint(0.5f, 0.10f);
        private static readonly CuiPoint CUserPageLblWetRtAnchor = new CuiPoint(0.975f, 0.14f);
        private static readonly CuiPoint CUserPageLblComfortLbAnchor = new CuiPoint(0.025f, 0.055f);
        private static readonly CuiPoint CUserPageLblComfortRtAnchor = new CuiPoint(0.5f, 0.095f);
        private static readonly CuiPoint CUserPageLblBleedLbAnchor = new CuiPoint(0.5f, 0.055f);
        private static readonly CuiPoint CUserPageLblBleedRtAnchor = new CuiPoint(0.975f, 0.095f);
        private static readonly CuiPoint CUserPageLblRads1LbAnchor = new CuiPoint(0.025f, 0.01f);
        private static readonly CuiPoint CUserPageLblRads1RtAnchor = new CuiPoint(0.5f, 0.05f);
        private static readonly CuiPoint CUserPageLblRads2LbAnchor = new CuiPoint(0.5f, 0.01f);
        private static readonly CuiPoint CUserPageLblRads2RtAnchor = new CuiPoint(0.975f, 0.05f);
        #endregion User page info label bounds
        #region User page button bounds
        // Row 1
        private static readonly CuiPoint CUserPageBtnBanLbAnchor = new CuiPoint(0.01f, 0.86f);
        private static readonly CuiPoint CUserPageBtnBanRtAnchor = new CuiPoint(0.16f, 0.92f);
        private static readonly CuiPoint CUserPageBtnKickLbAnchor = new CuiPoint(0.17f, 0.86f);
        private static readonly CuiPoint CUserPageBtnKickRtAnchor = new CuiPoint(0.32f, 0.92f);
        private static readonly CuiPoint CUserPageLblReasonLbAnchor = new CuiPoint(0.33f, 0.86f);
        private static readonly CuiPoint CUserPageLblReasonRtAnchor = new CuiPoint(0.48f, 0.92f);
        private static readonly CuiPoint CUserPagePanelReasonLbAnchor = new CuiPoint(0.49f, 0.86f);
        private static readonly CuiPoint CUserPagePanelReasonRtAnchor = new CuiPoint(0.99f, 0.92f);
        private static readonly CuiPoint CUserPageEdtReasonLbAnchor = new CuiPoint(0.005f, 0f);
        private static readonly CuiPoint CUserPageEdtReasonRtAnchor = new CuiPoint(0.995f, 1f);
        // Row 2
        private static readonly CuiPoint CUserPageBtnVUnmuteLbAnchor = new CuiPoint(0.01f, 0.78f);
        private static readonly CuiPoint CUserPageBtnVUnmuteRtAnchor = new CuiPoint(0.16f, 0.84f);
        private static readonly CuiPoint CUserPageBtnVMuteLbAnchor = new CuiPoint(0.17f, 0.78f);
        private static readonly CuiPoint CUserPageBtnVMuteRtAnchor = new CuiPoint(0.32f, 0.84f);
        // Row 3
        private static readonly CuiPoint CUserPageBtnCUnmuteLbAnchor = new CuiPoint(0.01f, 0.70f);
        private static readonly CuiPoint CUserPageBtnCUnmuteRtAnchor = new CuiPoint(0.16f, 0.76f);
        private static readonly CuiPoint CUserPageBtnCMuteLbAnchor = new CuiPoint(0.17f, 0.70f);
        private static readonly CuiPoint CUserPageBtnCMuteRtAnchor = new CuiPoint(0.32f, 0.76f);
        private static readonly CuiPoint CUserPageBtnCMuteFifteenLbAnchor = new CuiPoint(0.33f, 0.70f);
        private static readonly CuiPoint CUserPageBtnCMuteFifteenRtAnchor = new CuiPoint(0.48f, 0.76f);
        private static readonly CuiPoint CUserPageBtnCMuteThirtyLbAnchor = new CuiPoint(0.49f, 0.70f);
        private static readonly CuiPoint CUserPageBtnCMuteThirtyRtAnchor = new CuiPoint(0.64f, 0.76f);
        private static readonly CuiPoint CUserPageBtnCMuteSixtyLbAnchor = new CuiPoint(0.65f, 0.70f);
        private static readonly CuiPoint CUserPageBtnCMuteSixtyRtAnchor = new CuiPoint(0.80f, 0.76f);
        // Row 4
        private static readonly CuiPoint CUserPageBtnUnFreezeLbAnchor = new CuiPoint(0.01f, 0.62f);
        private static readonly CuiPoint CUserPageBtnUnFreezeRtAnchor = new CuiPoint(0.16f, 0.68f);
        private static readonly CuiPoint CUserPageBtnFreezeLbAnchor = new CuiPoint(0.17f, 0.62f);
        private static readonly CuiPoint CUserPageBtnFreezeRtAnchor = new CuiPoint(0.32f, 0.68f);
        // Row 5
        private static readonly CuiPoint CUserPageBtnClearInventoryLbAnchor = new CuiPoint(0.01f, 0.54f);
        private static readonly CuiPoint CUserPageBtnClearInventoryRtAnchor = new CuiPoint(0.16f, 0.60f);
        private static readonly CuiPoint CUserPageBtnResetBPLbAnchor = new CuiPoint(0.17f, 0.54f);
        private static readonly CuiPoint CUserPageBtnResetBPRtAnchor = new CuiPoint(0.32f, 0.60f);
        private static readonly CuiPoint CUserPageBtnResetMetabolismLbAnchor = new CuiPoint(0.33f, 0.54f);
        private static readonly CuiPoint CUserPageBtnResetMetabolismRtAnchor = new CuiPoint(0.48f, 0.60f);
        private static readonly CuiPoint CUserPageBtnRecoverMetabolismLbAnchor = new CuiPoint(0.49f, 0.54f);
        private static readonly CuiPoint CUserPageBtnRecoverMetabolismRtAnchor = new CuiPoint(0.64f, 0.60f);
        // Row 6
        private static readonly CuiPoint CUserPageBtnTeleportToLbAnchor = new CuiPoint(0.01f, 0.46f);
        private static readonly CuiPoint CUserPageBtnTeleportToRtAnchor = new CuiPoint(0.16f, 0.52f);
        private static readonly CuiPoint CUserPageBtnTeleportLbAnchor = new CuiPoint(0.17f, 0.46f);
        private static readonly CuiPoint CUserPageBtnTeleportRtAnchor = new CuiPoint(0.32f, 0.52f);
        private static readonly CuiPoint CUserPageBtnSpectateLbAnchor = new CuiPoint(0.33f, 0.46f);
        private static readonly CuiPoint CUserPageBtnSpectateRtAnchor = new CuiPoint(0.48f, 0.52f);
        // Row 7
        private static readonly CuiPoint CUserPageBtnPermsLbAnchor = new CuiPoint(0.01f, 0.38f);
        private static readonly CuiPoint CUserPageBtnPermsRtAnchor = new CuiPoint(0.16f, 0.44f);
        // Row 11
        private static readonly CuiPoint CUserPageBtnHurt25LbAnchor = new CuiPoint(0.01f, 0.10f);
        private static readonly CuiPoint CUserPageBtnHurt25RtAnchor = new CuiPoint(0.16f, 0.16f);
        private static readonly CuiPoint CUserPageBtnHurt50LbAnchor = new CuiPoint(0.17f, 0.10f);
        private static readonly CuiPoint CUserPageBtnHurt50RtAnchor = new CuiPoint(0.32f, 0.16f);
        private static readonly CuiPoint CUserPageBtnHurt75LbAnchor = new CuiPoint(0.33f, 0.10f);
        private static readonly CuiPoint CUserPageBtnHurt75RtAnchor = new CuiPoint(0.48f, 0.16f);
        private static readonly CuiPoint CUserPageBtnHurt100LbAnchor = new CuiPoint(0.49f, 0.10f);
        private static readonly CuiPoint CUserPageBtnHurt100RtAnchor = new CuiPoint(0.64f, 0.16f);
        private static readonly CuiPoint CUserPageBtnKillLbAnchor = new CuiPoint(0.65f, 0.10f);
        private static readonly CuiPoint CUserPageBtnKillRtAnchor = new CuiPoint(0.80f, 0.16f);
        // Row 12
        private static readonly CuiPoint CUserPageBtnHeal25LbAnchor = new CuiPoint(0.01f, 0.02f);
        private static readonly CuiPoint CUserPageBtnHeal25RtAnchor = new CuiPoint(0.16f, 0.08f);
        private static readonly CuiPoint CUserPageBtnHeal50LbAnchor = new CuiPoint(0.17f, 0.02f);
        private static readonly CuiPoint CUserPageBtnHeal50RtAnchor = new CuiPoint(0.32f, 0.08f);
        private static readonly CuiPoint CUserPageBtnHeal75LbAnchor = new CuiPoint(0.33f, 0.02f);
        private static readonly CuiPoint CUserPageBtnHeal75RtAnchor = new CuiPoint(0.48f, 0.08f);
        private static readonly CuiPoint CUserPageBtnHeal100LbAnchor = new CuiPoint(0.49f, 0.02f);
        private static readonly CuiPoint CUserPageBtnHeal100RtAnchor = new CuiPoint(0.64f, 0.08f);
        private static readonly CuiPoint CUserPageBtnHealWoundsLbAnchor = new CuiPoint(0.65f, 0.02f);
        private static readonly CuiPoint CUserPageBtnHealWoundsRtAnchor = new CuiPoint(0.80f, 0.08f);
        #endregion User page button bounds
        #endregion User page panel bounds

        #region Predefined UI elements
        private static readonly CuiPanel CBasePanel = new CuiPanel
        {
            RectTransform =
            {
                AnchorMin = "0 0",
                AnchorMax = "1 1",
                OffsetMin = "0 0",
                OffsetMax = "0 0"
            },
            CursorEnabled = true,
            Image = new CuiImageComponent { Color = CuiColor.None.ToString() }
        };

        private static readonly CuiPanel CMainPanel = new CuiPanel
        {
            RectTransform =
            {
                AnchorMin = CMainLbAnchor.ToString(),
                AnchorMax = CMainRtAnchor.ToString(),
                OffsetMin = "0 0",
                OffsetMax = "0 0"
            },
            CursorEnabled = true,
            Image = new CuiImageComponent { Color = CuiColor.BackgroundDark.ToString() }
        };

        private static readonly CuiPanel CTabHeaderPanel = new CuiPanel
        {
            RectTransform =
            {
                AnchorMin = CMainMenuHeaderContainerLbAnchor.ToString(),
                AnchorMax = CMainMenuHeaderContainerRtAnchor.ToString(),
                OffsetMin = "0 0",
                OffsetMax = "0 0"
            },
            CursorEnabled = true,
            Image = new CuiImageComponent { Color = CuiColor.None.ToString() }
        };

        private static readonly CuiPanel CTabTabBtnPanel = new CuiPanel
        {
            RectTransform =
            {
                AnchorMin = CMainMenuTabBtnContainerLbAnchor.ToString(),
                AnchorMax = CMainMenuTabBtnContainerRtAnchor.ToString(),
                OffsetMin = "0 0",
                OffsetMax = "0 0"
            },
            CursorEnabled = true,
            Image = new CuiImageComponent { Color = CuiColor.Background.ToString() }
        };

        private static readonly CuiPanel CMainPagePanel = new CuiPanel
        {
            RectTransform =
            {
                AnchorMin = CMainPanelLbAnchor.ToString(),
                AnchorMax = CMainPanelRtAnchor.ToString(),
                OffsetMin = "0 0",
                OffsetMax = "0 0"
            },
            CursorEnabled = true,
            Image = new CuiImageComponent { Color = CuiColor.Background.ToString() }
        };

        private static readonly CuiPanel CBanByIdGroupPanel = new CuiPanel
        {
            RectTransform =
            {
                AnchorMin = CMainPagePanelBanByIdLbAnchor.ToString(),
                AnchorMax = CMainPagePanelBanByIdRtAnchor.ToString(),
                OffsetMin = "0 0",
                OffsetMax = "0 0"
            },
            CursorEnabled = true,
            Image = new CuiImageComponent { Color = CuiColor.BackgroundDark.ToString() }
        };

        private static readonly CuiPanel CUserBtnPageSearchInputPanel = new CuiPanel
        {
            RectTransform =
            {
                AnchorMin = CUserBtnPagePanelSearchInputLbAnchor.ToString(),
                AnchorMax = CUserBtnPagePanelSearchInputRtAnchor.ToString(),
                OffsetMin = "0 0",
                OffsetMax = "0 0"
            },
            CursorEnabled = true,
            Image = new CuiImageComponent { Color = CuiColor.BackgroundDark.ToString() }
        };

        private static readonly CuiLabel CTabMenuHeaderLbl = new CuiLabel
        {
            Text =
            {
                Text = "Player Administration by ThibmoRozier",
                FontSize = 22,
                Align = TextAnchor.MiddleCenter,
                Color = CuiColor.TextTitle.ToString()
            },
            RectTransform =
            {
                AnchorMin = CMainMenuHeaderLblLbAnchor.ToString(),
                AnchorMax = CMainMenuHeaderLblRtAnchor.ToString(),
                OffsetMin = "0 0",
                OffsetMax = "0 0"
            }
        };

        private static readonly CuiLabel CMainPageTitleLbl = new CuiLabel
        {
            Text =
            {
                Text = "Main",
                FontSize = 18,
                Align = TextAnchor.MiddleLeft,
                Color = CuiColor.TextAlt.ToString()
            },
            RectTransform =
            {
                AnchorMin = CMainLblTitleLbAnchor.ToString(),
                AnchorMax = CMainLblTitleRtAnchor.ToString(),
                OffsetMin = "0 0",
                OffsetMax = "0 0"
            }
        };

        private static readonly CuiButton CTabMenuCloseBtn = new CuiButton
        {
            Button =
            {
                Command = CCloseUiCmd,
                Close = string.Empty,
                Color = CuiColor.ButtonDecline.ToString()
            },
            RectTransform =
            {
                AnchorMin = CMainMenuCloseBtnLbAnchor.ToString(),
                AnchorMax = CMainMenuCloseBtnRtAnchor.ToString(),
                OffsetMin = "0 0",
                OffsetMax = "0 0"
            },
            Text =
            {
                Text = "X",
                FontSize = 22,
                Align = TextAnchor.MiddleCenter,
                Color = CuiColor.TextAlt.ToString()
            }
        };

        private static readonly CuiButton CBanByIdActiveBtn = new CuiButton
        {
            Button =
            {
                Command = CMainPageBanByIdCmd,
                Close = string.Empty,
                Color = CuiColor.ButtonDanger.ToString()
            },
            RectTransform =
            {
                AnchorMin = CMainPageBtnBanByIdLbAnchor.ToString(),
                AnchorMax = CMainPageBtnBanByIdRtAnchor.ToString(),
                OffsetMin = "0 0",
                OffsetMax = "0 0"
            },
            Text =
            {
                Text = "Ban",
                FontSize = 14,
                Align = TextAnchor.MiddleCenter,
                Color = CuiColor.TextAlt.ToString()
            }
        };

        private static readonly CuiButton CBanByIdInactiveBtn = new CuiButton
        {
            Button =
            {
                Command = string.Empty,
                Close = string.Empty,
                Color = CuiColor.ButtonInactive.ToString()
            },
            RectTransform =
            {
                AnchorMin = CMainPageBtnBanByIdLbAnchor.ToString(),
                AnchorMax = CMainPageBtnBanByIdRtAnchor.ToString(),
                OffsetMin = "0 0",
                OffsetMax = "0 0"
            },
            Text =
            {
                Text = "Ban",
                FontSize = 14,
                Align = TextAnchor.MiddleCenter,
                Color = CuiColor.TextAlt.ToString()
            }
        };

        private static readonly CuiButton CUserBtnPagePreviousInactiveBtn = new CuiButton
        {
            Button =
            {
                Command = string.Empty,
                Close = string.Empty,
                Color = CuiColor.ButtonInactive.ToString()
            },
            RectTransform =
            {
                AnchorMin = CUserBtnPageBtnPreviousLbAnchor.ToString(),
                AnchorMax = CUserBtnPageBtnPreviousRtAnchor.ToString(),
                OffsetMin = "0 0",
                OffsetMax = "0 0"
            },
            Text =
            {
                Text = "<<",
                FontSize = 18,
                Align = TextAnchor.MiddleCenter,
                Color = CuiColor.TextAlt.ToString()
            }
        };

        private static readonly CuiButton CUserBtnPageNextInactiveBtn = new CuiButton
        {
            Button =
            {
                Command = string.Empty,
                Close = string.Empty,
                Color = CuiColor.ButtonInactive.ToString()
            },
            RectTransform =
            {
                AnchorMin = CUserBtnPageBtnNextLbAnchor.ToString(),
                AnchorMax = CUserBtnPageBtnNextRtAnchor.ToString(),
                OffsetMin = "0 0",
                OffsetMax = "0 0"
            },
            Text =
            {
                Text = ">>",
                FontSize = 18,
                Align = TextAnchor.MiddleCenter,
                Color = CuiColor.TextAlt.ToString()
            }
        };

        private static readonly CuiInputField CBanByIdEdt = new CuiInputField
        {
            InputField =
            {
                Text = string.Empty,
                FontSize = 14,
                Align = TextAnchor.MiddleLeft,
                Color = CuiColor.TextAlt.ToString(),
                CharsLimit = 24,
                Command = CMainPageBanIdInputTextCmd,
                IsPassword = false
            },
            RectTransform =
            {
                AnchorMin = CMainPageEdtBanByIdLbAnchor.ToString(),
                AnchorMax = CMainPageEdtBanByIdRtAnchor.ToString(),
                OffsetMin = "0 0",
                OffsetMax = "0 0"
            }
        };
        #endregion Predefined UI elements
        #endregion Constants

        #region Variables
        private static PlayerAdministration FPluginInstance;
        private ConfigData FConfigData;
        private Dictionary<ulong, string> FMainPageBanIdInputText = new Dictionary<ulong, string>();     // Format: <userId, text>
        private Dictionary<ulong, string> FUserBtnPageSearchInputText = new Dictionary<ulong, string>(); // Format: <userId, text>
        private Dictionary<ulong, string> FUserPageReasonInputText = new Dictionary<ulong, string>();    // Format: <userId, text>
        #endregion Variables

        #region Hooks
        void Loaded()
        {
            LoadConfig();
            permission.RegisterPermission(CPermUiShow, this);
            permission.RegisterPermission(CPermKick, this);
            permission.RegisterPermission(CPermBan, this);
            permission.RegisterPermission(CPermKill, this);
            permission.RegisterPermission(CPermPerms, this);
            permission.RegisterPermission(CPermVoiceMute, this);
            permission.RegisterPermission(CPermChatMute, this);
            permission.RegisterPermission(CPermFreeze, this);
            permission.RegisterPermission(CPermClearInventory, this);
            permission.RegisterPermission(CPermResetBP, this);
            permission.RegisterPermission(CPermResetMetabolism, this);
            permission.RegisterPermission(CPermRecoverMetabolism, this);
            permission.RegisterPermission(CPermHurt, this);
            permission.RegisterPermission(CPermHeal, this);
            permission.RegisterPermission(CPermTeleport, this);
            permission.RegisterPermission(CPermSpectate, this);
            permission.RegisterPermission(CPermDetailInfo, this);
            permission.RegisterPermission(CPermProtectBan, this);
            permission.RegisterPermission(CPermProtectHurt, this);
            permission.RegisterPermission(CPermProtectKick, this);
            permission.RegisterPermission(CPermProtectKill, this);
            permission.RegisterPermission(CPermProtectReset, this);
            FPluginInstance = this;

            if (UpgradeTo156())
                LogDebug("Upgraded the config to version 1.5.6");
        }

        void Unload()
        {
            foreach (BasePlayer player in Player.Players) {
                CuiHelper.DestroyUi(player, CBasePanelName);

                if (FMainPageBanIdInputText.ContainsKey(player.userID))
                    FMainPageBanIdInputText.Remove(player.userID);

                if (FUserBtnPageSearchInputText.ContainsKey(player.userID))
                    FUserBtnPageSearchInputText.Remove(player.userID);

                if (FUserPageReasonInputText.ContainsKey(player.userID))
                    FUserPageReasonInputText.Remove(player.userID);
            }

            FPluginInstance = null;
        }

        void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            if (FMainPageBanIdInputText.ContainsKey(player.userID))
                FMainPageBanIdInputText.Remove(player.userID);

            if (FUserBtnPageSearchInputText.ContainsKey(player.userID))
                FUserBtnPageSearchInputText.Remove(player.userID);

            if (FUserPageReasonInputText.ContainsKey(player.userID))
                FUserPageReasonInputText.Remove(player.userID);
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();

            try {
                FConfigData = Config.ReadObject<ConfigData>();

                if (FConfigData == null)
                    LoadDefaultConfig();

                if (UpgradeTo1310())
                    LogDebug("Upgraded the config to version 1.3.10");

                if (UpgradeTo1313())
                    LogDebug("Upgraded the config to version 1.3.13");
            } catch {
                LoadDefaultConfig();
            }

            SaveConfig();
        }

        protected override void LoadDefaultConfig()
        {
            FConfigData = new ConfigData
            {
                UsePermSystem = true,
                BanMsgWebhookUrl = string.Empty,
                KickMsgWebhookUrl = string.Empty
            };
            LogDebug("Default config loaded");
        }

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(
                new Dictionary<string, string>
                {
                    { "Permission Error Text", "You do not have the required permissions to use this command." },
                    { "Permission Error Log Text", "{0}: Tried to execute a command requiring the '{1}' permission" },
                    { "Kick Reason Message Text", "Administrative decision" },
                    { "Ban Reason Message Text", "Administrative decision" },
                    { "Protection Active Text", "Unable to perform this action, protection is enabled for this user" },

                    { "Never Label Text", "Never" },
                    { "Banned Label Text", " (Banned)" },
                    { "Dev Label Text", " (Developer)" },
                    { "Connected Label Text", "Connected" },
                    { "Disconnected Label Text", "Disconnected" },
                    { "Sleeping Label Text", "Sleeping" },
                    { "Awake Label Text", "Awake" },
                    { "Alive Label Text", "Alive" },
                    { "Dead Label Text", "Dead" },
                    { "Flying Label Text", " Flying" },
                    { "Mounted Label Text", " Mounted" },

                    { "User Button Page Title Text", "Click a username to go to the player's control page" },
                    { "User Page Title Format", "Control page for player '{0}'{1}" },

                    { "Ban By ID Title Text", "Ban a user by ID" },
                    { "Ban By ID Label Text", "User ID:" },
                    { "Search Label Text", "Search:" },
                    { "Player Info Label Text", "Player information:" },
                    { "Player Actions Label Text", "Player actions:" },

                    { "Id Label Format", "ID: {0}{1}" },
                    { "Auth Level Label Format", "Auth level: {0}" },
                    { "Connection Label Format", "Connection: {0}" },
                    { "Status Label Format", "Status: {0} and {1}" },
                    { "Flags Label Format", "Flags:{0}{1}" },
                    { "Position Label Format", "Position: {0}" },
                    { "Rotation Label Format", "Rotation: {0}" },
                    { "Last Admin Cheat Label Format", "Last admin cheat: {0}" },
                    { "Idle Time Label Format", "Idle time: {0} seconds" },
                    { "Economics Balance Label Format", "Balance: {0} coins" },
                    { "ServerRewards Points Label Format", "Reward points: {0}" },
                    { "Health Label Format", "Health: {0}" },
                    { "Calories Label Format", "Calories: {0}" },
                    { "Hydration Label Format", "Hydration: {0}" },
                    { "Temp Label Format", "Temperature: {0}" },
                    { "Wetness Label Format", "Wetness: {0}" },
                    { "Comfort Label Format", "Comfort: {0}" },
                    { "Bleeding Label Format", "Bleeding: {0}" },
                    { "Radiation Label Format", "Radiation: {0}" },
                    { "Radiation Protection Label Format", "Protection: {0}" },

                    { "Main Tab Text", "Main" },
                    { "Online Player Tab Text", "Online Players" },
                    { "Offline Player Tab Text", "Offline Players" },
                    { "Banned Player Tab Text", "Banned Players" },

                    { "Go Button Text", "Go" },

                    { "Unban Button Text", "Unban" },
                    { "Ban Button Text", "Ban" },
                    { "Kick Button Text", "Kick" },
                    { "Reason Input Label Text", "Reason:" },

                    { "Voice Unmute Button Text", "Unmute Voice" },
                    { "Voice Mute Button Text", "Mute Voice" },

                    { "Chat Unmute Button Text", "Unmute Chat" },
                    { "Chat Mute Button Text", "Mute Chat" },
                    { "Chat Mute Button Text 15", "Mute Chat 15 Min" },
                    { "Chat Mute Button Text 30", "Mute Chat 30 Min" },
                    { "Chat Mute Button Text 60", "Mute Chat 60 Min" },

                    { "UnFreeze Button Text", "UnFreeze" },
                    { "Freeze Button Text", "Freeze" },
                    { "Freeze Not Installed Button Text", "Freeze Not Installed" },

                    { "Clear Inventory Button Text", "Clear Inventory" },
                    { "Reset Blueprints Button Text", "Reset Blueprints" },
                    { "Reset Metabolism Button Text", "Reset Metabolism" },
                    { "Recover Metabolism Button Text", "Recover Metabolism" },

                    { "Teleport To Player Button Text", "Teleport To Player" },
                    { "Teleport Player Button Text", "Teleport Player" },
                    { "Spectate Player Button Text", "Spectate Player" },

                    { "Perms Button Text", "Permissions" },
                    { "Perms Not Installed Button Text", "Perms Not Installed" },

                    { "Hurt 25 Button Text", "Hurt 25" },
                    { "Hurt 50 Button Text", "Hurt 50" },
                    { "Hurt 75 Button Text", "Hurt 75" },
                    { "Hurt 100 Button Text", "Hurt 100" },
                    { "Kill Button Text", "Kill" },

                    { "Heal 25 Button Text", "Heal 25" },
                    { "Heal 50 Button Text", "Heal 50" },
                    { "Heal 75 Button Text", "Heal 75" },
                    { "Heal 100 Button Text", "Heal 100" },
                    { "Heal Wounds Button Text", "Heal Wounds" }
                }, this, "en"
            );
            LogDebug("Default messages loaded");
        }

        protected override void SaveConfig() => Config.WriteObject(FConfigData);
        #endregion Hooks

        #region Command Callbacks
        [Command(CPadminCmd)]
        private void PlayerAdministrationUICallback(IPlayer aPlayer, string aCommand, string[] aArgs)
        {
            BasePlayer player = BasePlayer.Find(aPlayer.Id);
            LogDebug("PlayerAdministrationUICallback was called");
            CuiHelper.DestroyUi(player, CBasePanelName);

            if (aPlayer.IsServer || !VerifyPermission(ref player, string.Empty, true))
                return;

            LogInfo($"{player.displayName}: Opened the menu");
            CuiHelper.AddUi(player, CuiHelper.ToJson(new CuiElementContainer {{ CBasePanel, Cui.ParentOverlay, CBasePanelName }}, false));
            BuildUI(player, UiPage.Main);
        }

        [Command(CCloseUiCmd)]
        private void PlayerAdministrationCloseUICallback(IPlayer aPlayer, string aCommand, string[] aArgs)
        {
            LogDebug("PlayerAdministrationCloseUICallback was called");

            if (aPlayer.IsServer)
                return;

            BasePlayer player = BasePlayer.Find(aPlayer.Id);
            CuiHelper.DestroyUi(player, CBasePanelName);

            if (FMainPageBanIdInputText.ContainsKey(player.userID))
                FMainPageBanIdInputText.Remove(player.userID);

            if (FUserBtnPageSearchInputText.ContainsKey(player.userID))
                FUserBtnPageSearchInputText.Remove(player.userID);
        }

        [Command(CSwitchUiCmd)]
        private void PlayerAdministrationSwitchUICallback(IPlayer aPlayer, string aCommand, string[] aArgs)
        {
            LogDebug("PlayerAdministrationSwitchUICallback was called");
            BasePlayer player = BasePlayer.Find(aPlayer.Id);

            if (aPlayer.IsServer || !VerifyPermission(ref player, string.Empty, true) || aArgs.Count() <= 0)
                return;

            bool twoOrMore = aArgs.Count() >= 2;

            switch (aArgs[0].ToLower()) {
                case CCmdArgPlayersOnline: {
                    BuildUI(player, UiPage.PlayersOnline, (twoOrMore ? aArgs[1] : string.Empty));
                    break;
                }
                case CCmdArgPlayersOnlineSearch: {
                    BuildUI(player, UiPage.PlayersOnline, (twoOrMore ? aArgs[1] : string.Empty), true);
                    break;
                }
                case CCmdArgPlayersOffline: {
                    BuildUI(player, UiPage.PlayersOffline, (twoOrMore ? aArgs[1] : string.Empty));
                    break;
                }
                case CCmdArgPlayersOfflineSearch: {
                    BuildUI(player, UiPage.PlayersOffline, (twoOrMore ? aArgs[1] : string.Empty), true);
                    break;
                }
                case CCmdArgPlayersBanned: {
                    BuildUI(player, UiPage.PlayersBanned, (twoOrMore ? aArgs[1] : string.Empty));
                    break;
                }
                case CCmdArgPlayersBannedSearch: {
                    BuildUI(player, UiPage.PlayersBanned, (twoOrMore ? aArgs[1] : string.Empty), true);
                    break;
                }
                case CCmdArgPlayerPage: {
                    BuildUI(player, UiPage.PlayerPage, (twoOrMore ? aArgs[1] : string.Empty));
                    break;
                }
                case CCmdArgPlayerPageBanned: {
                    BuildUI(player, UiPage.PlayerPageBanned, (twoOrMore ? aArgs[1] : string.Empty));
                    break;
                }
                default: { // Main is the default for everything
                    BuildUI(player, UiPage.Main);
                    break;
                }
            }
        }

        [Command(CUnbanUserCmd)]
        private void PlayerAdministrationUnbanUserCallback(IPlayer aPlayer, string aCommand, string[] aArgs)
        {
            LogDebug("PlayerAdministrationUnbanUserCallback was called");
            ulong targetId;

            if (!GetTargetFromArg(aArgs, out targetId))
                return;

            if (aPlayer.IsServer) {
                
                Player.Unban(targetId);
                LogInfo($"{aPlayer.Name}: Unbanned user ID {targetId}");
            } else {
                BasePlayer player = BasePlayer.Find(aPlayer.Id);

                if (!VerifyPermission(ref player, CPermBan, true))
                    return;

                Player.Unban(targetId);
                LogInfo($"{player.displayName}: Unbanned user ID {targetId}");
                timer.Once(0.01f, () => BuildUI(player, UiPage.Main));
            }
        }

        [Command(CBanUserCmd)]
        private void PlayerAdministrationBanUserCallback(IPlayer aPlayer, string aCommand, string[] aArgs)
        {
            LogDebug("PlayerAdministrationBanUserCallback was called");
            ulong targetId;

            if (!GetTargetFromArg(aArgs, out targetId))
                return;

            if (aPlayer.IsServer) {
                if (permission.UserHasPermission(targetId.ToString(), CPermProtectBan)) {
                    aPlayer.Reply(GetMessage("Protection Active Text", null));
                    return;
                }

                string banReasonMsg = GetMessage("Ban Reason Message Text", targetId.ToString());
                Player.Ban(targetId, banReasonMsg);
                LogInfo($"{aPlayer.Name}: Banned user ID {targetId}");
                SendDiscordKickBanMessage(aPlayer.Name, string.Empty, ServerUsers.Get(targetId).username, targetId.ToString(), banReasonMsg, true);
            } else {
                BasePlayer player = BasePlayer.Find(aPlayer.Id);

                if (!VerifyPermission(ref player, CPermBan, true))
                    return;

                if (permission.UserHasPermission(targetId.ToString(), CPermProtectBan)) {
                    SendMessage(ref player, GetMessage("Protection Active Text", player.UserIDString));
                    return;
                }

                string banReasonMsg;

                if (FUserPageReasonInputText.ContainsKey(player.userID)) {
                    banReasonMsg = FUserPageReasonInputText[player.userID].Trim();

                    if (string.IsNullOrEmpty(banReasonMsg))
                        banReasonMsg = GetMessage("Ban Reason Message Text", targetId.ToString());
                } else {
                    banReasonMsg = GetMessage("Ban Reason Message Text", targetId.ToString());
                }

                Player.Ban(targetId, banReasonMsg);
                LogInfo($"{player.displayName}: Banned user ID {targetId}");
                SendDiscordKickBanMessage(player.displayName, player.UserIDString, ServerUsers.Get(targetId).username, targetId.ToString(), banReasonMsg, true);
                timer.Once(0.01f, () => BuildUI(player, UiPage.PlayerPage, targetId.ToString()));
            }
        }

        [Command(CMainPageBanByIdCmd)]
        private void PlayerAdministrationMainPageBanByIdCallback(IPlayer aPlayer, string aCommand, string[] aArgs)
        {
            LogDebug("PlayerAdministrationMainPageBanByIdCallback was called");
            BasePlayer player = BasePlayer.Find(aPlayer.Id);
            ulong targetId;

            if (
                aPlayer.IsServer ||
                !VerifyPermission(ref player, CPermBan, true) || !FMainPageBanIdInputText.ContainsKey(player.userID) ||
                !ulong.TryParse(FMainPageBanIdInputText[player.userID], out targetId)
            )
                return;

            if (permission.UserHasPermission(targetId.ToString(), CPermProtectBan)) {
                SendMessage(ref player, GetMessage("Protection Active Text", player.UserIDString));
                return;
            }

            string banReasonMsg = GetMessage("Ban Reason Message Text", targetId.ToString());
            Player.Ban(targetId, banReasonMsg);
            ServerUsers.User targetPlayer = ServerUsers.Get(targetId);
            LogInfo($"{player.displayName}: Banned user ID {targetId}");
            SendDiscordKickBanMessage(player.displayName, player.UserIDString, targetPlayer.username, targetId.ToString(), banReasonMsg, true);
            timer.Once(0.01f, () => BuildUI(player, UiPage.Main));
        }

        [Command(CKickUserCmd)]
        private void PlayerAdministrationKickUserCallback(IPlayer aPlayer, string aCommand, string[] aArgs)
        {
            LogDebug("PlayerAdministrationKickUserCallback was called");
            ulong targetId;

            if (!GetTargetFromArg(aArgs, out targetId))
                return;

            BasePlayer targetPlayer = BasePlayer.FindByID(targetId);
            string kickReasonMsg = GetMessage("Kick Reason Message Text", targetId.ToString());

            if (aPlayer.IsServer) {
                if (permission.UserHasPermission(targetId.ToString(), CPermProtectKick)) {
                    aPlayer.Reply(GetMessage("Protection Active Text", null));
                    return;
                }

                targetPlayer?.Kick(kickReasonMsg);
                LogInfo($"{aPlayer.Name}: Kicked user ID {targetId}");
                SendDiscordKickBanMessage(aPlayer.Name, string.Empty, targetPlayer.displayName, targetPlayer.UserIDString, kickReasonMsg, false);
            } else {
                BasePlayer player = BasePlayer.Find(aPlayer.Id);

                if (!VerifyPermission(ref player, CPermKick, true))
                    return;

                if (permission.UserHasPermission(targetId.ToString(), CPermProtectKick)) {
                    SendMessage(ref player, GetMessage("Protection Active Text", player.UserIDString));
                    return;
                }
                
                targetPlayer?.Kick(kickReasonMsg);
                LogInfo($"{player.displayName}: Kicked user ID {targetId}");
                SendDiscordKickBanMessage(player.displayName, player.UserIDString, targetPlayer.displayName, targetPlayer.UserIDString, kickReasonMsg, false);
                timer.Once(0.01f, () => BuildUI(player, UiPage.PlayerPage, targetId.ToString()));
            }
        }

        [Command(CVoiceUnmuteUserCmd)]
        private void PlayerAdministrationVoiceUnmuteUserCallback(IPlayer aPlayer, string aCommand, string[] aArgs)
        {
            LogDebug("PlayerAdministrationVoiceUnmuteUserCallback was called");
            ulong targetId;

            if (!GetTargetFromArg(aArgs, out targetId))
                return;

            if (aPlayer.IsServer) {
                (BasePlayer.FindByID(targetId) ?? BasePlayer.FindSleeping(targetId))?.SetPlayerFlag(BasePlayer.PlayerFlags.VoiceMuted, false);
                LogInfo($"{aPlayer.Name}: Voice unmuted user ID {targetId}");
            } else {
                BasePlayer player = BasePlayer.Find(aPlayer.Id);

                if (!VerifyPermission(ref player, CPermVoiceMute, true))
                    return;

                (BasePlayer.FindByID(targetId) ?? BasePlayer.FindSleeping(targetId))?.SetPlayerFlag(BasePlayer.PlayerFlags.VoiceMuted, false);
                LogInfo($"{player.displayName}: Voice unmuted user ID {targetId}");
                timer.Once(0.01f, () => BuildUI(player, UiPage.PlayerPage, targetId.ToString()));
            }
        }

        [Command(CVoiceMuteUserCmd)]
        private void PlayerAdministrationVoiceMuteUserCallback(IPlayer aPlayer, string aCommand, string[] aArgs)
        {
            LogDebug("PlayerAdministrationVoiceMuteUserCallback was called");
            ulong targetId;

            if (!GetTargetFromArg(aArgs, out targetId))
                return;

            if (aPlayer.IsServer) {
                (BasePlayer.FindByID(targetId) ?? BasePlayer.FindSleeping(targetId))?.SetPlayerFlag(BasePlayer.PlayerFlags.VoiceMuted, true);
                LogInfo($"{aPlayer.Name}: Voice muted user ID {targetId}");
            } else {
                BasePlayer player = BasePlayer.Find(aPlayer.Id);

                if (!VerifyPermission(ref player, CPermVoiceMute, true))
                    return;

                (BasePlayer.FindByID(targetId) ?? BasePlayer.FindSleeping(targetId))?.SetPlayerFlag(BasePlayer.PlayerFlags.VoiceMuted, true);
                LogInfo($"{player.displayName}: Voice muted user ID {targetId}");
                timer.Once(0.01f, () => BuildUI(player, UiPage.PlayerPage, targetId.ToString()));
            }
        }

        [Command(CChatUnmuteUserCmd)]
        private void PlayerAdministrationChatUnmuteUserCallback(IPlayer aPlayer, string aCommand, string[] aArgs)
        {
            LogDebug("PlayerAdministrationChatUnmuteUserCallback was called");
            ulong targetId;

            if (!GetTargetFromArg(aArgs, out targetId))
                return;

            if (aPlayer.IsServer) {
                BasePlayer target = BasePlayer.FindByID(targetId) ?? BasePlayer.FindSleeping(targetId);

                if (BetterChatMute != null && target != null) {
                    BetterChatMute.Call("API_Unmute", target.IPlayer, aPlayer);
                } else {
                    target?.SetPlayerFlag(BasePlayer.PlayerFlags.ChatMute, false);
                }

                LogInfo($"{aPlayer.Name}: Chat unmuted user ID {targetId}");
            } else {
                BasePlayer player = BasePlayer.Find(aPlayer.Id);

                if (!VerifyPermission(ref player, CPermChatMute, true))
                    return;

                BasePlayer target = BasePlayer.FindByID(targetId) ?? BasePlayer.FindSleeping(targetId);

                if (BetterChatMute != null && target != null) {
                    BetterChatMute.Call("API_Unmute", target.IPlayer, aPlayer);
                } else {
                    target?.SetPlayerFlag(BasePlayer.PlayerFlags.ChatMute, false);
                }

                LogInfo($"{player.displayName}: Chat unmuted user ID {targetId}");
                timer.Once(0.01f, () => BuildUI(player, UiPage.PlayerPage, targetId.ToString()));
            }
        }

        [Command(CChatMuteUserCmd)]
        private void PlayerAdministrationChatMuteUserCallback(IPlayer aPlayer, string aCommand, string[] aArgs)
        {
            LogDebug("PlayerAdministrationChatMuteUserCallback was called");
            ulong targetId;
            float time;

            if (!GetTargetAmountFromArg(aArgs, out targetId, out time))
                return;

            if (aPlayer.IsServer) {
                BasePlayer target = BasePlayer.FindByID(targetId) ?? BasePlayer.FindSleeping(targetId);

                if (BetterChatMute != null && target != null) {
                    if (time == 0f) {
                        BetterChatMute.Call("API_Mute", target.IPlayer, aPlayer);
                    } else {
                        BetterChatMute.Call("API_TimeMute", target.IPlayer, aPlayer, TimeSpan.FromMinutes(time));
                    }
                } else {
                    target?.SetPlayerFlag(BasePlayer.PlayerFlags.ChatMute, true);
                }

                LogInfo($"{aPlayer.Name}: Chat muted user ID {targetId}");
            } else {
                BasePlayer player = BasePlayer.Find(aPlayer.Id);

                if (!VerifyPermission(ref player, CPermChatMute, true))
                    return;

                BasePlayer target = BasePlayer.FindByID(targetId) ?? BasePlayer.FindSleeping(targetId);

                if (BetterChatMute != null && target != null) {
                    if (time == 0f) {
                        BetterChatMute.Call("API_Mute", target.IPlayer, aPlayer);
                    } else {
                        BetterChatMute.Call("API_TimeMute", target.IPlayer, player.IPlayer, TimeSpan.FromMinutes(time));
                    }
                } else {
                    target?.SetPlayerFlag(BasePlayer.PlayerFlags.ChatMute, true);
                }

                LogInfo($"{player.displayName}: Chat muted user ID {targetId}");
                timer.Once(0.01f, () => BuildUI(player, UiPage.PlayerPage, targetId.ToString()));
            }
        }

        [Command(CUnreezeCmd)]
        private void PlayerAdministrationUnfreezeCallback(IPlayer aPlayer, string aCommand, string[] aArgs)
        {
            LogDebug("PlayerAdministrationUnfreezeCallback was called");
            BasePlayer player = BasePlayer.Find(aPlayer.Id);
            ulong targetId;

            if (aPlayer.IsServer || !VerifyPermission(ref player, CPermFreeze, true) || !GetTargetFromArg(aArgs, out targetId))
                return;

            player.SendConsoleCommand($"{CFreezeUnfreezeCmd} {targetId}");
            LogInfo($"{player.displayName}: Chat unfroze user ID {targetId}");
            // Let code execute, then reload screen
            timer.Once(0.1f, () => BuildUI(player, UiPage.PlayerPage, targetId.ToString()));
        }

        [Command(CFreezeCmd)]
        private void PlayerAdministrationFreezeCallback(IPlayer aPlayer, string aCommand, string[] aArgs)
        {
            LogDebug("PlayerAdministrationFreezeCallback was called");
            BasePlayer player = BasePlayer.Find(aPlayer.Id);
            ulong targetId;

            if (aPlayer.IsServer || !VerifyPermission(ref player, CPermFreeze, true) || !GetTargetFromArg(aArgs, out targetId))
                return;

            player.SendConsoleCommand($"{CFreezeFreezeCmd} {targetId}");
            LogInfo($"{player.displayName}: Chat froze user ID {targetId}");
            // Let code execute, then reload screen
            timer.Once(0.1f, () => BuildUI(player, UiPage.PlayerPage, targetId.ToString()));
        }

        [Command(CClearUserInventoryCmd)]
        private void PlayerAdministrationClearUserInventoryCallback(IPlayer aPlayer, string aCommand, string[] aArgs)
        {
            LogDebug("PlayerAdministrationClearUserInventoryCallback was called");
            ulong targetId;

            if (!GetTargetFromArg(aArgs, out targetId))
                return;

            if (aPlayer.IsServer) {
                if (permission.UserHasPermission(targetId.ToString(), CPermProtectReset)) {
                    aPlayer.Reply(GetMessage("Protection Active Text", null));
                    return;
                }

                (BasePlayer.FindByID(targetId) ?? BasePlayer.FindSleeping(targetId))?.inventory.Strip();
                LogInfo($"{aPlayer.Name}: Cleared the inventory of user ID {targetId}");
            } else {
                BasePlayer player = BasePlayer.Find(aPlayer.Id);

                if (!VerifyPermission(ref player, CPermClearInventory, true))
                    return;

                if (permission.UserHasPermission(targetId.ToString(), CPermProtectReset)) {
                    SendMessage(ref player, GetMessage("Protection Active Text", player.UserIDString));
                    return;
                }

                (BasePlayer.FindByID(targetId) ?? BasePlayer.FindSleeping(targetId))?.inventory.Strip();
                LogInfo($"{player.displayName}: Cleared the inventory of user ID {targetId}");
                timer.Once(0.01f, () => BuildUI(player, UiPage.PlayerPage, targetId.ToString()));
            }
        }

        [Command(CResetUserBPCmd)]
        private void PlayerAdministrationResetUserBlueprintsCallback(IPlayer aPlayer, string aCommand, string[] aArgs)
        {
            LogDebug("PlayerAdministrationResetUserBlueprintsCallback was called");
            ulong targetId;

            if (!GetTargetFromArg(aArgs, out targetId))
                return;

            if (aPlayer.IsServer) {
                if (permission.UserHasPermission(targetId.ToString(), CPermProtectReset)) {
                    aPlayer.Reply(GetMessage("Protection Active Text", null));
                    return;
                }

                (BasePlayer.FindByID(targetId) ?? BasePlayer.FindSleeping(targetId))?.blueprints.Reset();
                LogInfo($"{aPlayer.Name}: Reset the blueprints of user ID {targetId}");
            } else {
                BasePlayer player = BasePlayer.Find(aPlayer.Id);

                if (!VerifyPermission(ref player, CPermResetBP, true))
                    return;

                if (permission.UserHasPermission(targetId.ToString(), CPermProtectReset)) {
                    SendMessage(ref player, GetMessage("Protection Active Text", player.UserIDString));
                    return;
                }

                (BasePlayer.FindByID(targetId) ?? BasePlayer.FindSleeping(targetId))?.blueprints.Reset();
                LogInfo($"{player.displayName}: Reset the blueprints of user ID {targetId}");
                timer.Once(0.01f, () => BuildUI(player, UiPage.PlayerPage, targetId.ToString()));
            }
        }

        [Command(CResetUserMetabolismCmd)]
        private void PlayerAdministrationResetUserMetabolismCallback(IPlayer aPlayer, string aCommand, string[] aArgs)
        {
            LogDebug("PlayerAdministrationResetUserMetabolismCallback was called");
            ulong targetId;

            if (!GetTargetFromArg(aArgs, out targetId))
                return;

            if (aPlayer.IsServer) {
                if (permission.UserHasPermission(targetId.ToString(), CPermProtectReset)) {
                    aPlayer.Reply(GetMessage("Protection Active Text", null));
                    return;
                }

                (BasePlayer.FindByID(targetId) ?? BasePlayer.FindSleeping(targetId))?.metabolism.Reset();
                LogInfo($"{aPlayer.Name}: Reset the metabolism of user ID {targetId}");
            } else {
                BasePlayer player = BasePlayer.Find(aPlayer.Id);

                if (!VerifyPermission(ref player, CPermResetMetabolism, true))
                    return;

                if (permission.UserHasPermission(targetId.ToString(), CPermProtectReset)) {
                    SendMessage(ref player, GetMessage("Protection Active Text", player.UserIDString));
                    return;
                }

                (BasePlayer.FindByID(targetId) ?? BasePlayer.FindSleeping(targetId))?.metabolism.Reset();
                LogInfo($"{player.displayName}: Reset the metabolism of user ID {targetId}");
                timer.Once(0.01f, () => BuildUI(player, UiPage.PlayerPage, targetId.ToString()));
            }
        }

        [Command(CRecoverUserMetabolismCmd)]
        private void PlayerAdministrationRecoverUserMetabolismCallback(IPlayer aPlayer, string aCommand, string[] aArgs)
        {
            LogDebug("PlayerAdministrationRecoverUserMetabolismCallback was called");
            ulong targetId;
            BasePlayer player = null;

            if (!GetTargetFromArg(aArgs, out targetId))
                return;

            if (!aPlayer.IsServer) {
                player = BasePlayer.Find(aPlayer.Id);

                if (!VerifyPermission(ref player, CPermRecoverMetabolism, true))
                    return;
            }

            PlayerMetabolism playerState = (BasePlayer.FindByID(targetId) ?? BasePlayer.FindSleeping(targetId))?.metabolism;
            playerState.bleeding.value = playerState.bleeding.min;
            playerState.calories.value = playerState.calories.max;
            playerState.comfort.value = 0;
            playerState.hydration.value = playerState.hydration.max;
            playerState.oxygen.value = playerState.oxygen.max;
            playerState.poison.value = playerState.poison.min;
            playerState.radiation_level.value = playerState.radiation_level.min;
            playerState.radiation_poison.value = playerState.radiation_poison.min;
            playerState.temperature.value = (PlayerMetabolism.HotThreshold + PlayerMetabolism.ColdThreshold) / 2;
            playerState.wetness.value = playerState.wetness.min;

            if (aPlayer.IsServer) {
                LogInfo($"{aPlayer.Name}: Recovered the metabolism of user ID {targetId}");
            } else {
                LogInfo($"{player.displayName}: Recovered the metabolism of user ID {targetId}");
                timer.Once(0.01f, () => BuildUI(player, UiPage.PlayerPage, targetId.ToString()));
            }
        }

        [Command(CTeleportToUserCmd)]
        private void PlayerAdministrationTeleportToUserCallback(IPlayer aPlayer, string aCommand, string[] aArgs)
        {
            LogDebug("PlayerAdministrationTeleportToUserCallback was called");
            BasePlayer player = BasePlayer.Find(aPlayer.Id);
            ulong targetId;

            if (aPlayer.IsServer || !VerifyPermission(ref player, CPermTeleport, true) || !GetTargetFromArg(aArgs, out targetId))
                return;

            player.Teleport(BasePlayer.FindByID(targetId) ?? BasePlayer.FindSleeping(targetId));
            LogInfo($"{player.displayName}: Teleported to user ID {targetId}");
            timer.Once(0.01f, () => BuildUI(player, UiPage.PlayerPage, targetId.ToString()));
        }

        [Command(CTeleportUserCmd)]
        private void PlayerAdministrationTeleportUserCallback(IPlayer aPlayer, string aCommand, string[] aArgs)
        {
            LogDebug("PlayerAdministrationTeleportUserCallback was called");
            BasePlayer player = BasePlayer.Find(aPlayer.Id);
            ulong targetId;

            if (aPlayer.IsServer || !VerifyPermission(ref player, CPermTeleport, true) || !GetTargetFromArg(aArgs, out targetId))
                return;

            BasePlayer targetPlayer = BasePlayer.FindByID(targetId) ?? BasePlayer.FindSleeping(targetId);
            targetPlayer.Teleport(player);
            LogInfo($"{targetPlayer.displayName}: Teleported to admin {player.displayName}");
            timer.Once(0.01f, () => BuildUI(player, UiPage.PlayerPage, targetId.ToString()));
        }

        [Command(CSpectateUserCmd)]
        private void PlayerAdministrationSpectateUserCallback(IPlayer aPlayer, string aCommand, string[] aArgs)
        {
            LogDebug("PlayerAdministrationSpectateUserCallback was called");
            BasePlayer player = BasePlayer.Find(aPlayer.Id);
            ulong targetId;

            if (aPlayer.IsServer || !VerifyPermission(ref player, CPermSpectate, true) || !GetTargetFromArg(aArgs, out targetId))
                return;

            if (!player.IsDead())
                player.DieInstantly();

            player.StartSpectating();
            player.UpdateSpectateTarget(targetId.ToString());
            LogInfo($"{player.displayName}: Started spectating user ID {targetId}");
            timer.Once(0.01f, () => BuildUI(player, UiPage.PlayerPage, targetId.ToString()));
        }

        [Command(CPermsCmd)]
        private void PlayerAdministrationRunPermsCallback(IPlayer aPlayer, string aCommand, string[] aArgs)
        {
            LogDebug("PlayerAdministrationRunPermsCallback was called");
            BasePlayer player = BasePlayer.Find(aPlayer.Id);
            ulong targetId;

            if (aPlayer.IsServer || !VerifyPermission(ref player, CPermPerms, true) || !GetTargetFromArg(aArgs, out targetId))
                return;

            player.SendConsoleCommand($"chat.say \"/{CPermsPermsCmd} {targetId}\"");
            timer.Once(0.01f, () => LogInfo($"{player.displayName}: Opened the permissions manager for user ID {targetId}"));
        }

        [Command(CHurtUserCmd)]
        private void PlayerAdministrationHurtUserCallback(IPlayer aPlayer, string aCommand, string[] aArgs)
        {
            LogDebug("PlayerAdministrationHurtUserCallback was called");
            ulong targetId;
            float amount;

            if (!GetTargetAmountFromArg(aArgs, out targetId, out amount))
                return;

            if (aPlayer.IsServer) {
                if (permission.UserHasPermission(targetId.ToString(), CPermProtectHurt)) {
                    aPlayer.Reply(GetMessage("Protection Active Text", null));
                    return;
                }

                (BasePlayer.FindByID(targetId) ?? BasePlayer.FindSleeping(targetId))?.Hurt(amount);
                LogInfo($"{aPlayer.Name}: Hurt user ID {targetId} for {amount} points");
            } else {
                BasePlayer player = BasePlayer.Find(aPlayer.Id);

                if (!VerifyPermission(ref player, CPermHurt, true))
                    return;

                if (permission.UserHasPermission(targetId.ToString(), CPermProtectHurt)) {
                    SendMessage(ref player, GetMessage("Protection Active Text", player.UserIDString));
                    return;
                }

                (BasePlayer.FindByID(targetId) ?? BasePlayer.FindSleeping(targetId))?.Hurt(amount);
                LogInfo($"{player.displayName}: Hurt user ID {targetId} for {amount} points");
                timer.Once(0.01f, () => BuildUI(player, UiPage.PlayerPage, targetId.ToString()));
            }
        }

        [Command(CKillUserCmd)]
        private void PlayerAdministrationKillUserCallback(IPlayer aPlayer, string aCommand, string[] aArgs)
        {
            LogDebug("PlayerAdministrationKillUserCallback was called");
            ulong targetId;

            if (!GetTargetFromArg(aArgs, out targetId))
                return;

            if (aPlayer.IsServer) {
                if (permission.UserHasPermission(targetId.ToString(), CPermProtectKill)) {
                    aPlayer.Reply(GetMessage("Protection Active Text", null));
                    return;
                }

                (BasePlayer.FindByID(targetId) ?? BasePlayer.FindSleeping(targetId))?.Die();
                LogInfo($"{aPlayer.Name}: Killed user ID {targetId}");
            } else {
                BasePlayer player = BasePlayer.Find(aPlayer.Id);

                if (!VerifyPermission(ref player, CPermKill, true))
                    return;

                if (permission.UserHasPermission(targetId.ToString(), CPermProtectKill)) {
                    SendMessage(ref player, GetMessage("Protection Active Text", player.UserIDString));
                    return;
                }

                (BasePlayer.FindByID(targetId) ?? BasePlayer.FindSleeping(targetId))?.Die();
                LogInfo($"{player.displayName}: Killed user ID {targetId}");
                timer.Once(0.01f, () => BuildUI(player, UiPage.PlayerPage, targetId.ToString()));
            }
        }

        [Command(CHealUserCmd)]
        private void PlayerAdministrationHealUserCallback(IPlayer aPlayer, string aCommand, string[] aArgs)
        {
            LogDebug("PlayerAdministrationHealUserCallback was called");
            ulong targetId;
            float amount;

            if (!GetTargetAmountFromArg(aArgs, out targetId, out amount))
                return;

            BasePlayer targetPlayer = BasePlayer.FindByID(targetId) ?? BasePlayer.FindSleeping(targetId);

            if (aPlayer.IsServer) {
                if (targetPlayer.IsWounded())
                    targetPlayer.StopWounded();

                targetPlayer.Heal(amount);
                LogInfo($"{aPlayer.Name}: Healed user ID {targetId} for {amount} points");
            } else {
                BasePlayer player = BasePlayer.Find(aPlayer.Id);

                if (!VerifyPermission(ref player, CPermHeal, true))
                    return;

                if (targetPlayer.IsWounded())
                    targetPlayer.StopWounded();

                targetPlayer.Heal(amount);
                LogInfo($"{player.displayName}: Healed user ID {targetId} for {amount} points");
                timer.Once(0.01f, () => BuildUI(player, UiPage.PlayerPage, targetId.ToString()));
            }
        }
        #endregion Command Callbacks

        #region Text Update Callbacks
        [Command(CMainPageBanIdInputTextCmd)]
        private void PlayerAdministrationMainPageBanIdInputTextCallback(IPlayer aPlayer, string aCommand, string[] aArgs)
        {
            BasePlayer player = BasePlayer.Find(aPlayer.Id);

            if (aPlayer.IsServer || !VerifyPermission(ref player, CPermUiShow) || aArgs.Count() <= 0) {
                if (FMainPageBanIdInputText.ContainsKey(player.userID))
                    FMainPageBanIdInputText.Remove(player.userID);

                return;
            }

            if (FMainPageBanIdInputText.ContainsKey(player.userID)) {
                FMainPageBanIdInputText[player.userID] = aArgs[0];
            } else {
                FMainPageBanIdInputText.Add(player.userID, aArgs[0]);
            }
        }

        [Command(CUserBtnPageSearchInputTextCmd)]
        private void PlayerAdministrationUserBtnPageSearchInputTextCallback(IPlayer aPlayer, string aCommand, string[] aArgs)
        {
            BasePlayer player = BasePlayer.Find(aPlayer.Id);

            if (aPlayer.IsServer || !VerifyPermission(ref player, CPermUiShow) || aArgs.Count() <= 0) {
                if (FUserBtnPageSearchInputText.ContainsKey(player.userID))
                    FUserBtnPageSearchInputText.Remove(player.userID);

                return;
            }

            if (FUserBtnPageSearchInputText.ContainsKey(player.userID)) {
                FUserBtnPageSearchInputText[player.userID] = aArgs[0];
            } else {
                FUserBtnPageSearchInputText.Add(player.userID, aArgs[0]);
            }
        }

        [Command(CUserPageReasonInputTextCmd)]
        private void PlayerAdministrationUserPageReasonInputTextCallback(IPlayer aPlayer, string aCommand, string[] aArgs)
        {
            BasePlayer player = BasePlayer.Find(aPlayer.Id);

            if (aPlayer.IsServer || !VerifyPermission(ref player, CPermUiShow) || aArgs.Count() <= 0) {
                if (FUserPageReasonInputText.ContainsKey(player.userID))
                    FUserPageReasonInputText.Remove(player.userID);

                return;
            }

            if (FUserPageReasonInputText.ContainsKey(player.userID)) {
                FUserPageReasonInputText[player.userID] = aArgs[0];
            } else {
                FUserPageReasonInputText.Add(player.userID, aArgs[0]);
            }
        }
        #endregion Text Update Callbacks
    }
}

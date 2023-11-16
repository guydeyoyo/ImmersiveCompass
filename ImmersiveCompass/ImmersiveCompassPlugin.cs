using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using ServerSync;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Security.Policy;
using UnityEngine;
using UnityEngine.UI;
using static Minimap;


namespace ImmersiveCompass
{
    [BepInPlugin(PLUGIN_GUID, PLUGIN_NAME, PLUGIN_VERSION)]
    public class ImmersiveCompassPlugin : BaseUnityPlugin
    {
        #region Variables
        internal const string PLUGIN_NAME = "ImmersiveCompass";
        internal const string PLUGIN_VERSION = "1.3.2";
        internal const string PLUGIN_GUID = "Yoyo." + PLUGIN_NAME;
        internal const string PLUGIN_AUTHOR = "gdragon";

        private static string ConfigFileName = PLUGIN_GUID + ".cfg";
        private static string ConfigFileFullPath = Paths.ConfigPath + Path.DirectorySeparatorChar + ConfigFileName;

        internal static readonly ManualLogSource ImmersiveCompassLogger = BepInEx.Logging.Logger.CreateLogSource(PLUGIN_NAME);

        private static ImmersiveCompassPlugin _context;
        private readonly Harmony _harmony = new(PLUGIN_GUID);

        internal static GameObject _objectCompass;
        internal static GameObject _objectPins;
        internal static GameObject _objectParent;
        internal static GameObject _objectCenterMark;

        internal static string[] _ignoredNames;
        internal static string[] _ignoredTypes;

        private static bool _gotCompassImage = false;
        private static bool _gotCompassMask = false;
        private static bool _gotCompassCenter = false;
        internal static bool _keyPressed = false;


#endregion Variables


        #region ServerSync
        internal static string ConnectionError = string.Empty;
        private static readonly ConfigSync configSync = new(PLUGIN_GUID) { DisplayName = PLUGIN_NAME, CurrentVersion = PLUGIN_VERSION, MinimumRequiredVersion = PLUGIN_VERSION };
        #endregion ServerSync


        #region Standard Methods
        private void Awake()
        {
            _context = this;

            #region Configuration
            _configEnabled = config("1 - Immersive Compass", "Enabled", true, "Enable or disable the Immersive Compass.", true);
            _configServerSync = config("1 - Immersive Compass", "ServerSync", true, "Enable or disable ServerSync (server will override this and relevant settings).", true);

            _compassUsePlayerDirection = config("2 - Compass Display", "Use Player Direction", false, "Orient the compass based on the direction the player is facing, rather than the middle of the screen.", false);
            _compassScale = config("2 - Compass Display", "Scale (Compass)", 0.75f, "Enlarge or shrink the scale of the compass.", false);
            _compassYOffset = config("2 - Compass Display", "Offset (Y)", 0, "Offset from the top of the screen in pixels.", false);
            _distancePinsMin = config("2 - Compass Display", "Distance (Minimum)", 1, "Minimum distance from pin to show on compass.", true);
            _distancePinsMax = config("2 - Compass Display", "Distance (Maximum)", 300, "Maximum distance from pin to show on compass.", true);
            _compassShowPlayerPins = config("2 - Compass Display", "Show Player Pins", true, "Show player pins on the compass.", true);
            _scalePins = config("2 - Compass Display", "Scale (Pins)", 1f, "Enlarge or shrink the overall scale of pins.", false);
            _scalePinsMin = config("2 - Compass Display", "Minimum Pin Size", 0.25f, "Enlarge or shrink the scale of the pins at their furthest visible distance.", false);
            _compassShowCenterMark = config("2 - Compass Display", "Show Center Mark", false, "(Optional) Show center mark graphic.", false);

            _ignoredPinNames = config("3 - Ignore", "Pin Names", "Silver,Obsidian,Copper,Tin", "Ignore location pins with these names (comma separated, no spaces). End a string with asterix * to denote a prefix.", true);
            _ignoredPinTypes = config("3 - Ignore", "Pin Types", "Shout,Ping", "Ignore location pins of these types (comma separated, no spaces). Types include: Icon0,Icon1,Icon2,Icon3,Icon4,Death,Bed,Shout,None,Boss,Player,RandomEvent,Ping,EventArea.", true);

            _fileCompass = config("4 - File System", "Compass", "compass.png", "Image file located in mod folder to use for compass overlay.", false);
            _fileMask = config("4 - File System", "Mask", "mask.png", "Image file located in mod folder to use as compass mask.", false);
            _fileOverlay = config("4 - File System", "Overlay", "", "(Optional) Image file located in mod folder to display on top the compass (eg. a decorative frame).", false);
            _fileUnderlay = config("4 - File System", "Underlay", "", "(Optional) Image file located in mod folder to display below the compass (eg. a decorative frame).", false);
            _fileCenter = config("4 - File System", "Center Mark", "center.png", "(Optional) Image file located in mod folder to display at center of compass (eg. a line).", false);

            _colorCompass = config("5 - Color Adjustment", "Color (Compass)", Color.white, "(Optional) Adjust the color of the compass.", false);
            _colorPins = config("5 - Color Adjustment", "Color (Pins)", Color.white, "(Optional) Adjust the color of the location pins.", false);
            _colorCenterMark = config("5 - Color Adjustment", "Color (Center Mark)", Color.yellow, "(Optional) Adjust the color of the center mark graphic.", false);

            configSync.AddLockingConfigEntry(_configServerSync);
            #endregion Configuration

            SetupWatcher();

            if (_configEnabled.Value == true) { ImmersiveCompassLogger.LogDebug($"{PLUGIN_GUID} v{PLUGIN_VERSION} enabled in configuration."); }
            else { ImmersiveCompassLogger.LogDebug($"{PLUGIN_GUID} v{PLUGIN_VERSION} not enabled in configuration."); return; }

            _ignoredPinNames.SettingChanged += SettingChanged_IgnoredNames;
            _ignoredPinTypes.SettingChanged += SettingChanged_IgnoredTypes;

            _ignoredNames = _ignoredPinNames.Value.Trim().Split(',');
            _ignoredTypes = _ignoredPinTypes.Value.Trim().Split(',');

            Assembly assembly = Assembly.GetExecutingAssembly();
            _harmony.PatchAll(assembly);
        }



        private void Start()
        {
            Game.isModded = true;
        }


        private void OnDestroy()
        {
            Config.Save();
            _harmony?.UnpatchSelf();
        }

        #endregion Standard Methods

        #region Compass Methods
        [HarmonyPatch(typeof(Hud), "Awake")]
        internal static class HudAwakeCompassPatch
        {
            internal static void Postfix(Hud __instance)
            {
                string _path = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location));

                #region Load Images from Disk
                ImmersiveCompassLogger.LogDebug("Starting attempt to load image files from disk.");

                Texture2D textureCompass = new Texture2D(2, 2, TextureFormat.RGBA32, true, true);
                if (_fileCompass.Value.Trim().Length > 0 && _fileCompass.Value.Trim().Length < 32768)
                {
                    try
                    {
                        byte[] _bytesCompass = File.ReadAllBytes(Path.Combine(_path, _fileCompass.Value.Trim()));
                        _gotCompassImage = textureCompass.LoadImage(_bytesCompass);
                    }
                    catch (Exception ex)
                    {
                        ImmersiveCompassLogger.LogError($"Invalid image file for compass: {_fileCompass.Value.Trim()}: {ex}");
                        return;
                    }
                }

                if (!_gotCompassImage)
                {
                    ImmersiveCompassLogger.LogDebug($"Could not find compass image file: {_fileCompass.Value.Trim()}");
                    return;
                }

                Texture2D textureMask = new Texture2D(2, 2, TextureFormat.RGBA32, true, true);
                if (_fileMask.Value.Trim().Length > 0 && _fileMask.Value.Trim().Length < 32768)
                {
                    try
                    {
                        byte[] _bytesMask = File.ReadAllBytes(Path.Combine(_path, _fileMask.Value.Trim()));
                        _gotCompassMask = textureMask.LoadImage(_bytesMask);
                    }
                    catch (Exception ex)
                    {
                        ImmersiveCompassLogger.LogError($"Invalid image file for mask: {_fileMask.Value.Trim()}: {ex}");
                        return;
                    }
                }

                Texture2D textureCenter = new Texture2D(0, 0, TextureFormat.RGBA32, true, true);
                if (_compassShowCenterMark.Value == true && _fileCenter.Value.Trim().Length > 0 && _fileCenter.Value.Trim().Length < 32768)
                {
                    try
                    {
                        byte[] _bytesCenter = File.ReadAllBytes(Path.Combine(_path, _fileCenter.Value.Trim()));
                        _gotCompassCenter = textureCenter.LoadImage(_bytesCenter);
                    }
                    catch (Exception ex)
                    {
                        ImmersiveCompassLogger.LogError($"Invalid image file for center: {_fileCenter.Value.Trim()}: {ex}");
                        return;
                    }
                }

                Texture2D textureOverlay = new Texture2D(0, 0, TextureFormat.RGBA32, true, true);
                bool _gotOverlay = false;
                if (_fileOverlay.Value.Trim().Length > 0 && _fileOverlay.Value.Trim().Length < 32768)
                {
                    try
                    {
                        byte[] _bytesOverlay = File.ReadAllBytes(Path.Combine(_path, _fileOverlay.Value.Trim()));
                        _gotOverlay = textureOverlay.LoadImage(_bytesOverlay);
                    }
                    catch (Exception ex)
                    {
                        ImmersiveCompassLogger.LogError($"Invalid image file for overlay: {_fileOverlay.Value.Trim()}: {ex}");
                        return;
                    }
                }

                Texture2D textureUnderlay = new Texture2D(0, 0, TextureFormat.RGBA32, true, true);
                bool _gotUnderlay = false;
                if (_fileUnderlay.Value.Trim().Length > 0 && _fileUnderlay.Value.Trim().Length < 32768)
                {
                    try
                    {
                        byte[] _bytesUnderlay = File.ReadAllBytes(Path.Combine(_path, _fileUnderlay.Value.Trim()));
                        _gotUnderlay = textureUnderlay.LoadImage(_bytesUnderlay);
                    }
                    catch (Exception ex)
                    {
                        ImmersiveCompassLogger.LogError($"Invalid image file for overlay: {_fileUnderlay.Value.Trim()}: {ex}");
                        return;
                    }
                }

                ImmersiveCompassLogger.LogDebug("Finished attempting to load image files from disk.");
                #endregion Load Images from Disk


                if (textureCompass.width < 1)
                {
                    ImmersiveCompassLogger.LogDebug("Image for compass was invalid or zero pixels in width.");
                    return;
                }

                float _halfWidthOfCompass = textureCompass.width / 2f;
                Sprite spriteCompass = Sprite.Create(textureCompass, new Rect(0, 0, textureCompass.width, textureCompass.height), Vector2.zero);

                Sprite spriteMask = null;
                if (_gotCompassMask && textureMask.width > 0)
                {
                    spriteMask = Sprite.Create(textureMask, new Rect(0, 0, _halfWidthOfCompass, textureMask.height), Vector2.zero);
                }

                Sprite spriteCenter = null;
                if (_compassShowCenterMark.Value == true && _gotCompassImage && _gotCompassMask && _gotCompassCenter && textureCenter.width > 0)
                {
                    spriteCenter = Sprite.Create(textureCenter, new Rect(0, 0, textureCenter.width, textureCenter.height), Vector2.zero);
                }

                Sprite spriteOverlay = null;
                if (_gotOverlay && textureOverlay.width > 0)
                {
                    spriteOverlay = Sprite.Create(textureOverlay, new Rect(0, 0, textureOverlay.width, textureOverlay.height), Vector2.zero);
                }

                Sprite spriteUnderlay = null;
                if (_gotUnderlay && textureUnderlay.width > 0)
                {
                    spriteUnderlay = Sprite.Create(textureUnderlay, new Rect(0, 0, textureUnderlay.width, textureUnderlay.height), Vector2.zero);
                }


                _objectParent = new GameObject();
                _objectParent.name = "Compass";
                RectTransform rectTransform = _objectParent.AddComponent<RectTransform>();
                rectTransform.SetParent(__instance.m_rootObject.transform);


                // Object: Overlay
                GameObject _objectOverlay = new GameObject();
                if (textureOverlay != null && textureOverlay.width > 0)
                {
                    _objectOverlay.name = "Overlay";
                    RectTransform _rectTransformOverlay = _objectOverlay.AddComponent<RectTransform>();
                    _rectTransformOverlay.SetParent(_objectParent.transform);
                    _rectTransformOverlay.localScale = Vector3.one * _compassScale.Value;
                    _rectTransformOverlay.sizeDelta = new Vector2(textureOverlay.width, textureOverlay.height);
                    _rectTransformOverlay.anchoredPosition = Vector2.zero;
                    Image _imageOverlay = _objectOverlay.AddComponent<Image>();
                    _imageOverlay.sprite = spriteOverlay;
                    _imageOverlay.preserveAspect = true;
                }

                // Object: Underlay
                GameObject _objectUnderlay = new GameObject();
                if (textureUnderlay != null && textureUnderlay.width > 0)
                {
                    _objectUnderlay.name = "Underlay";
                    RectTransform _rectTransformUnderlay = _objectUnderlay.AddComponent<RectTransform>();
                    _rectTransformUnderlay.SetParent(_objectParent.transform);
                    _rectTransformUnderlay.localScale = Vector3.one * _compassScale.Value;
                    _rectTransformUnderlay.sizeDelta = new Vector2(textureUnderlay.width, textureUnderlay.height);
                    _rectTransformUnderlay.anchoredPosition = Vector2.zero;
                    Image _imageUnderlay = _objectUnderlay.AddComponent<Image>();
                    _imageUnderlay.sprite = spriteUnderlay;
                    _imageUnderlay.preserveAspect = true;
                }

                // Object: Mask
                GameObject _objectMask = new GameObject();
                if (textureMask != null && textureMask.width > 0)
                {
                    _objectMask.name = "Mask";
                    RectTransform _rectTransformMask = _objectMask.AddComponent<RectTransform>();
                    _rectTransformMask.SetParent(_objectParent.transform);
                    _rectTransformMask.sizeDelta = new Vector2(_halfWidthOfCompass, textureCompass.height);
                    _rectTransformMask.localScale = Vector3.one * _compassScale.Value;
                    _rectTransformMask.anchoredPosition = Vector2.zero;

                    Image _imageMask = _objectMask.AddComponent<Image>();
                    _imageMask.sprite = spriteMask;
                    _imageMask.preserveAspect = true;

                    Mask _mask = _objectMask.AddComponent<Mask>();
                    _mask.showMaskGraphic = false;
                }

                // Object: Compass
                _objectCompass = new GameObject();
                _objectCompass.name = "Image";

                RectTransform _rectTransformCompass = _objectCompass.AddComponent<RectTransform>();
                _rectTransformCompass.SetParent(_objectMask.transform);
                _rectTransformCompass.localScale = Vector3.one;
                _rectTransformCompass.anchoredPosition = Vector2.zero;
                _rectTransformCompass.sizeDelta = new Vector2(textureCompass.width, textureCompass.height);

                Image image = _objectCompass.AddComponent<Image>();
                image.sprite = spriteCompass;
                image.preserveAspect = true;

                // Object: Center Mark
                if (_compassShowCenterMark.Value == true && textureCenter.width > 0)
                {
                    _objectCenterMark = new GameObject();
                    _objectCenterMark.name = "CenterMark";

                    RectTransform _rectTransformCenter = _objectCenterMark.AddComponent<RectTransform>();
                    _rectTransformCenter.SetParent(_objectMask.transform);
                    _rectTransformCenter.localScale = Vector3.one;
                    _rectTransformCenter.anchoredPosition = Vector2.zero;
                    _rectTransformCenter.sizeDelta = new Vector2(textureCenter.width, textureCenter.height);

                    Image imageCenter = _objectCenterMark.AddComponent<Image>();
                    imageCenter.sprite = spriteCenter;
                    imageCenter.preserveAspect = true;
                }

                // Object: Pins
                _objectPins = new GameObject();
                _objectPins.name = "Pins";
                _rectTransformCompass = _objectPins.AddComponent<RectTransform>();
                _rectTransformCompass.SetParent(_objectMask.transform);
                _rectTransformCompass.localScale = Vector3.one;
                _rectTransformCompass.anchoredPosition = Vector2.zero;
                _rectTransformCompass.sizeDelta = new Vector2(_halfWidthOfCompass, textureCompass.height);

                ImmersiveCompassLogger.LogDebug("Finished attempting to add compass to game hud.");
            }
        }


        [HarmonyPatch(typeof(Hud), "Update")]
        internal static class HudUpdateCompassPatch
        {
            internal static void Prefix(Hud __instance)
            {
                if (_configEnabled.Value != true || !Player.m_localPlayer) return;
                if (!_gotCompassImage || !_gotCompassMask) return;

                float _angle;

                if (_compassUsePlayerDirection.Value == true)
                {
                    _angle = Player.m_localPlayer.transform.eulerAngles.y;
                }
                else
                {
                    _angle = GameCamera.instance.transform.eulerAngles.y;
                }

                if (_angle > 180) _angle -= 360;

                _angle *= -Mathf.Deg2Rad;

                Rect _rectCompass = _objectCompass.GetComponent<Image>().sprite.rect;

                float _imageScale = 1;
                CanvasScaler cs = GuiScaler.m_scalers?.Find(x => x.m_canvasScaler.name == "LoadingGUI")?.m_canvasScaler;
                if (cs != null) _imageScale = cs.scaleFactor;

                _objectCompass.GetComponent<RectTransform>().localPosition = Vector3.right * (_rectCompass.width / 2) * _angle / (2f * Mathf.PI) - new Vector3(_rectCompass.width * 0.125f, 0, 0);

                _objectCompass.GetComponent<Image>().color = _colorCompass.Value;
                _objectParent.GetComponent<RectTransform>().localScale = Vector3.one * _compassScale.Value;
                _objectParent.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, (Screen.height / _imageScale - _objectCompass.GetComponent<Image>().sprite.texture.height * _compassScale.Value) / 2) - Vector2.up * _compassYOffset.Value;

                if (_compassShowCenterMark.Value == true && _objectCenterMark != null)
                {
                    _objectCenterMark.GetComponent<Image>().color = _colorCenterMark.Value;
                    _objectCenterMark.SetActive(true);
                }


                int count = _objectPins.transform.childCount;
                List<string> __oldPins = new List<string>();
                foreach (Transform t in _objectPins.transform)
                    __oldPins.Add(t.name);

                var __listPins = new List<Minimap.PinData>();
                __listPins.AddRange(AccessTools.DeclaredField(typeof(Minimap), "m_pins").GetValue(Minimap.instance) as List<Minimap.PinData>);

                __listPins.AddRange((AccessTools.DeclaredField(typeof(Minimap), "m_locationPins").GetValue(Minimap.instance) as Dictionary<Vector3, Minimap.PinData>).Values);

                if (_compassShowPlayerPins.Value == true)
                {
                    __listPins.AddRange((AccessTools.DeclaredField(typeof(Minimap), "m_playerPins").GetValue(Minimap.instance) as List<Minimap.PinData>));
                }

                Minimap.PinData deathPin = AccessTools.DeclaredField(typeof(Minimap), "m_deathPin").GetValue(Minimap.instance) as Minimap.PinData;

                if (deathPin != null)
                {
                    __listPins.Add(deathPin);
                }

                Transform _transformPlayer = Player.m_localPlayer.transform;
                float zeroScaleDistance = 0;

                if (1f - _scalePinsMin.Value > 0) { zeroScaleDistance = _distancePinsMax.Value / (1 - _scalePinsMin.Value); }

                foreach (Minimap.PinData pin in __listPins)
                {
                    string name = pin.m_pos.ToString();
                    __oldPins.Remove(name);

                    var _findTransform = _objectPins.transform.Find(name);
                    if (_ignoredNames.Contains(pin.m_name) || _ignoredTypes.Contains(pin.m_type.ToString()) || (_ignoredPinNames.Value.Contains("*") && Array.Exists(_ignoredNames, s => s.EndsWith("*") && name.StartsWith(s.Substring(0, s.Length - 1)))) || Vector3.Distance(_transformPlayer.position, pin.m_pos) > _distancePinsMax.Value || Vector3.Distance(_transformPlayer.position, pin.m_pos) < _distancePinsMin.Value)
                    {
                        if (_findTransform)
                            _findTransform.gameObject.SetActive(false);
                        continue;
                    }
                    if (_findTransform)
                        _findTransform.gameObject.SetActive(true);

                    Vector3 offset;
                    if (_compassUsePlayerDirection.Value)
                        offset = _transformPlayer.InverseTransformPoint(pin.m_pos);
                    else
                        offset = GameCamera.instance.transform.InverseTransformPoint(pin.m_pos);

                    _angle = Mathf.Atan2(offset.x, offset.z);

                    GameObject po;
                    RectTransform rt;
                    Image img;

                    if (!_findTransform)
                    {
                        po = new GameObject();
                        po.name = pin.m_pos.ToString();
                        rt = po.AddComponent<RectTransform>();
                        rt.SetParent(_objectPins.transform);
                        rt.anchoredPosition = Vector2.zero;
                        img = po.AddComponent<Image>();
                    }
                    else
                    {
                        po = _findTransform.gameObject;
                        rt = _findTransform.GetComponent<RectTransform>();
                        img = _findTransform.GetComponent<Image>();
                    }

                    float distanceScale = _scalePinsMin.Value < 1 ? (zeroScaleDistance - Vector3.Distance(_transformPlayer.position, pin.m_pos)) / zeroScaleDistance : 1;
                    rt.localScale = Vector3.one * distanceScale * 0.5f * _scalePins.Value;
                    img.color = _colorPins.Value;
                    img.sprite = pin.m_icon;
                    rt.localPosition = Vector3.right * (_rectCompass.width / 2) * _angle / (2f * Mathf.PI);
                }

                foreach (string name in __oldPins)
                    Destroy(_objectPins.transform.Find(name).gameObject);
            }
        }
        #endregion Compass Methods


        #region Configuration Watcher
        private void SetupWatcher()
        {
            FileSystemWatcher watcher = new(Paths.ConfigPath, ConfigFileName);
            watcher.Changed += ReadConfigValues;
            watcher.Created += ReadConfigValues;
            watcher.Renamed += ReadConfigValues;
            watcher.IncludeSubdirectories = true;
            watcher.SynchronizingObject = ThreadingHelper.SynchronizingObject;
            watcher.EnableRaisingEvents = true;
        }


        private void ReadConfigValues(object sender, FileSystemEventArgs e)
        {
            if (!File.Exists(ConfigFileFullPath)) return;
            try
            {
                ImmersiveCompassLogger.LogDebug("ReadConfigValues called.");
                Config.Reload();
            }
            catch
            {
                ImmersiveCompassLogger.LogError($"There was an issue loading file: {ConfigFileName}");
                ImmersiveCompassLogger.LogError($"Check your config entries for correct spelling and format.");
            }
        }

        private void SettingChanged_IgnoredNames(object sender, EventArgs e)
        {
            _ignoredNames = _ignoredPinNames.Value.Trim().Split(',');
        }

        private void SettingChanged_IgnoredTypes(object sender, EventArgs e)
        {
            _ignoredTypes = _ignoredPinTypes.Value.Trim().Split(',');
        }
        #endregion Configuration Watcher


        #region Configuration Options
        #nullable enable
        internal static ConfigEntry<bool> _configEnabled = null!;
        internal static ConfigEntry<bool> _configServerSync = null!;

        internal static ConfigEntry<string>? _fileCompass = null!;
        internal static ConfigEntry<string>? _fileOverlay = null!;
        internal static ConfigEntry<string>? _fileUnderlay = null!;
        internal static ConfigEntry<string>? _fileMask = null!;
        internal static ConfigEntry<string>? _fileCenter = null!;

        internal static ConfigEntry<Color> _colorCompass = null!;
        internal static ConfigEntry<Color> _colorPins = null!;
        internal static ConfigEntry<Color> _colorCenterMark = null!;

        internal static ConfigEntry<bool> _compassUsePlayerDirection = null!;
        internal static ConfigEntry<int> _compassYOffset = null!;
        internal static ConfigEntry<float> _compassScale = null!;
        internal static ConfigEntry<int> _distancePinsMin = null!;
        internal static ConfigEntry<int> _distancePinsMax = null!;
        internal static ConfigEntry<float> _scalePinsMin = null!;
        internal static ConfigEntry<float> _scalePins = null!;
        internal static ConfigEntry<bool> _compassShowPlayerPins = null!;
        internal static ConfigEntry<bool> _compassShowCenterMark = null!;

        internal static ConfigEntry<string> _ignoredPinNames = null!;
        internal static ConfigEntry<string> _ignoredPinTypes = null!;
        #nullable disable

        private ConfigEntry<T> config<T>(string group, string name, T value, ConfigDescription description, bool synchronizedSetting = true)
        {
            ConfigDescription extendedDescription = new(description.Description + (synchronizedSetting ? " [ServerSync]":""), description.AcceptableValues, description.Tags);
            ConfigEntry<T> configEntry = Config.Bind(group, name, value, extendedDescription);

            if (synchronizedSetting)
            {
                SyncedConfigEntry<T> syncedConfigEntry = configSync.AddConfigEntry(configEntry);
                syncedConfigEntry.SynchronizedConfig = synchronizedSetting;
            }

            return configEntry;
        }

        private ConfigEntry<T> config<T>(string group, string name, T value, string description, bool synchronizedSetting = true)
        {
            return config(group, name, value, new ConfigDescription(description), synchronizedSetting);
        }

        private class ConfigurationManagerAttributes
        {
            public bool? Browsable = false;
        }

        class AcceptableShortcuts : AcceptableValueBase
        {
            public AcceptableShortcuts() : base(typeof(KeyboardShortcut)) { }
            public override object Clamp(object value) => value;
            public override bool IsValid(object value) => true;

            public override string ToDescriptionString() => "# Acceptable values: " + string.Join(", ", KeyboardShortcut.AllKeyCodes);
        }
        #endregion Configuration Options

    }

}

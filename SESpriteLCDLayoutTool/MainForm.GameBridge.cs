using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using SESpriteLCDLayoutTool.Models;

namespace SESpriteLCDLayoutTool
{
    public partial class MainForm
    {
        private const int GameBridgeExportIntervalMs = 150;
        private ToolStripMenuItem _mnuGameBridgeToggle;
        private Timer _gameBridgeTimer;
        private int _lastGameBridgeHash;
        private string _gameBridgePath;
        private DateTime _lastGameBridgeStatusUtc;
        private bool _gameBridgeDirty;
        private bool _gameBridgeWriteInProgress;
        private string _pendingGameBridgeFrame;
        private int _pendingGameBridgeSpriteCount;

        private void ToggleGameBridgeExport()
        {
            if (_gameBridgeTimer != null)
            {
                _gameBridgeTimer.Stop();
                _gameBridgeTimer.Dispose();
                _gameBridgeTimer = null;
                _pendingGameBridgeFrame = null;
                _gameBridgeWriteInProgress = false;
                if (_mnuGameBridgeToggle != null)
                    _mnuGameBridgeToggle.Text = "Start Export To Game LCD";
                SetStatus("Game LCD bridge stopped");
                return;
            }

            _gameBridgePath = GetDefaultGameBridgePath();
            Directory.CreateDirectory(Path.GetDirectoryName(_gameBridgePath));

            _gameBridgeTimer = new Timer();
            _gameBridgeTimer.Interval = GameBridgeExportIntervalMs;
            _gameBridgeTimer.Tick += (s, e) => WriteGameBridgeFrame();
            _gameBridgeDirty = true;
            _gameBridgeTimer.Start();

            if (_mnuGameBridgeToggle != null)
                _mnuGameBridgeToggle.Text = "Stop Export To Game LCD";

            WriteGameBridgeFrame();
            SetStatus("Game LCD bridge writing to " + _gameBridgePath);
        }

        private void MarkGameBridgeDirty()
        {
            if (_gameBridgeTimer != null)
                _gameBridgeDirty = true;
        }

        private static string GetDefaultGameBridgePath()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "SpaceEngineers", "Storage", "Grid Schematics_GridSchematics", "GridSchematics_LiveBridge.txt");
        }

        private void WriteGameBridgeFrame()
        {
            try
            {
                if (!_gameBridgeDirty || _layout == null || _layout.Sprites == null)
                    return;

                _gameBridgeDirty = false;
                string frame = SerializeGameBridgeFrame(_layout);
                int hash = frame.GetHashCode();
                if (hash == _lastGameBridgeHash)
                    return;

                _lastGameBridgeHash = hash;
                QueueGameBridgeWrite(frame, CountVisibleBridgeSprites(_layout));
            }
            catch (Exception ex)
            {
                SetStatus("Game LCD bridge export failed: " + ex.Message);
            }
        }

        private void QueueGameBridgeWrite(string frame, int spriteCount)
        {
            _pendingGameBridgeFrame = frame;
            _pendingGameBridgeSpriteCount = spriteCount;
            if (!_gameBridgeWriteInProgress)
                StartNextGameBridgeWrite();
        }

        private void StartNextGameBridgeWrite()
        {
            if (_gameBridgeTimer == null || string.IsNullOrEmpty(_pendingGameBridgeFrame))
            {
                _gameBridgeWriteInProgress = false;
                return;
            }

            string frame = _pendingGameBridgeFrame;
            int spriteCount = _pendingGameBridgeSpriteCount;
            string path = _gameBridgePath;
            _pendingGameBridgeFrame = null;
            _gameBridgeWriteInProgress = true;

            Task.Run(() =>
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, frame, Encoding.UTF8);
            }).ContinueWith(t =>
            {
                if (IsDisposed) return;
                BeginInvoke(new Action(() =>
                {
                    if (t.Exception != null)
                    {
                        SetStatus("Game LCD bridge write failed: " + t.Exception.GetBaseException().Message);
                    }
                    else if ((DateTime.UtcNow - _lastGameBridgeStatusUtc).TotalMilliseconds >= 1000)
                    {
                        _lastGameBridgeStatusUtc = DateTime.UtcNow;
                        SetStatus("Game LCD bridge updated: " + spriteCount + " sprite(s)");
                    }

                    _gameBridgeWriteInProgress = false;
                    if (!string.IsNullOrEmpty(_pendingGameBridgeFrame))
                        StartNextGameBridgeWrite();
                }));
            });
        }

        private static string SerializeGameBridgeFrame(LcdLayout layout)
        {
            var sb = new StringBuilder(8192);
            sb.Append("GSLIVEBRIDGE|1").AppendLine();
            sb.Append("SURFACE|")
                .Append(layout.SurfaceWidth.ToString(CultureInfo.InvariantCulture))
                .Append('|')
                .Append(layout.SurfaceHeight.ToString(CultureInfo.InvariantCulture))
                .AppendLine();

            foreach (var sp in layout.Sprites)
            {
                if (sp == null || sp.IsHidden)
                    continue;

                bool isText = sp.Type == SpriteEntryType.Text;
                string data = isText ? sp.Text : sp.SpriteName;
                string font = isText ? sp.FontId : string.Empty;
                float rotationOrScale = isText ? sp.Scale : sp.Rotation;

                sb.Append("S|")
                    .Append(isText ? 'T' : 'X').Append('|')
                    .Append(EncodeBridgeString(data)).Append('|')
                    .Append(F(sp.X)).Append('|')
                    .Append(F(sp.Y)).Append('|')
                    .Append(F(sp.Width)).Append('|')
                    .Append(F(sp.Height)).Append('|')
                    .Append(ByteString(sp.ColorR)).Append('|')
                    .Append(ByteString(sp.ColorG)).Append('|')
                    .Append(ByteString(sp.ColorB)).Append('|')
                    .Append(ByteString(sp.ColorA)).Append('|')
                    .Append(F(rotationOrScale)).Append('|')
                    .Append(EncodeBridgeString(font)).Append('|')
                    .Append(AlignmentCode(sp.Alignment))
                    .AppendLine();
            }

            return sb.ToString();
        }

        private static int CountVisibleBridgeSprites(LcdLayout layout)
        {
            int count = 0;
            if (layout == null || layout.Sprites == null)
                return 0;

            foreach (var sp in layout.Sprites)
                if (sp != null && !sp.IsHidden)
                    count++;
            return count;
        }

        private static string EncodeBridgeString(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
        }

        private static string F(float value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        private static string ByteString(int value)
        {
            if (value < 0) value = 0;
            if (value > 255) value = 255;
            return value.ToString(CultureInfo.InvariantCulture);
        }

        private static string AlignmentCode(SpriteTextAlignment alignment)
        {
            if (alignment == SpriteTextAlignment.Left) return "L";
            if (alignment == SpriteTextAlignment.Right) return "R";
            return "C";
        }
    }
}
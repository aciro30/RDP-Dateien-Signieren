using System.Drawing;
using System.ComponentModel;
using System.Diagnostics;
using System.Security;
using System.Security.Cryptography.X509Certificates;

namespace RdpSignTool.WinForms;

internal sealed class MainForm : Form
{
    private const int LeftPanelMinWidth = 360;
    private const int RightPanelMinWidth = 320;
    private static readonly Color AppBackgroundColor = Color.FromArgb(236, 241, 247);
    private static readonly Color CardBackgroundColor = Color.White;
    private static readonly Color AccentColor = Color.FromArgb(24, 90, 204);
    private static readonly Color AccentSoftColor = Color.FromArgb(240, 246, 255);
    private static readonly Color SuccessColor = Color.FromArgb(22, 128, 76);
    private static readonly Color BorderColor = Color.FromArgb(211, 220, 232);
    private static readonly Color MutedTextColor = Color.FromArgb(92, 103, 117);
    private static readonly Color TitleTextColor = Color.FromArgb(29, 38, 52);
    private static readonly Color LogBackgroundColor = Color.FromArgb(19, 24, 33);
    private static readonly Color LogTextColor = Color.Gainsboro;
    private static readonly Color StatusNeutralColor = Color.FromArgb(66, 78, 92);
    private static readonly Color WarningColor = Color.FromArgb(173, 108, 15);
    private static readonly Color ErrorColor = Color.FromArgb(180, 64, 64);

    private readonly RdpSigningService _service = new();
    private readonly SplitContainer _mainSplitContainer = new();
    private readonly RichTextBox _logBox = new();
    private readonly ComboBox _certificateComboBox = new();
    private readonly ListBox _fileListBox = new();
    private readonly Label _environmentLabel = new();
    private readonly Label _warningPolicyStatusLabel = new();
    private readonly Panel _dropZonePanel = new();
    private readonly Label _dropZoneLabel = new();
    private readonly Label _fileStatusLabel = new();
    private readonly Button _refreshCertificatesButton = new();
    private readonly Button _newCertificateButton = new();
    private readonly Button _exportCertificateButton = new();
    private readonly Button _enableWarningSuppressionButton = new();
    private readonly Button _disableWarningSuppressionButton = new();
    private readonly Button _browseButton = new();
    private readonly Button _removeButton = new();
    private readonly Button _clearButton = new();
    private readonly Button _signButton = new();

    private readonly HashSet<string> _selectedFiles = new(StringComparer.OrdinalIgnoreCase);

    private RdpSigningService.AppConfig _config = new(null, null, null);
    private string? _rdpSignPath;
    private string _hashMode = "sha1";
    private bool _isBusy;

    public MainForm()
    {
        Text = "RDP-Signierer";
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(1280, 820);
        MinimumSize = new Size(980, 680);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = AppBackgroundColor;
        Font = new Font("Segoe UI", 9F, FontStyle.Regular);
        AllowDrop = true;

        try
        {
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        }
        catch
        {
        }

        BuildLayout();

        Shown += (_, _) => InitializeApplication();
    }

    private void BuildLayout()
    {
        _mainSplitContainer.Dock = DockStyle.Fill;
        _mainSplitContainer.Orientation = Orientation.Vertical;
        _mainSplitContainer.SplitterWidth = 10;
        _mainSplitContainer.BackColor = AppBackgroundColor;
        _mainSplitContainer.Panel1.Padding = new Padding(12);
        _mainSplitContainer.Panel2.Padding = new Padding(12);

        Controls.Add(_mainSplitContainer);

        var leftCard = CreateCardPanel();
        var rightCard = CreateCardPanel();
        _mainSplitContainer.Panel1.Controls.Add(leftCard);
        _mainSplitContainer.Panel2.Controls.Add(rightCard);

        var logLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3
        };
        logLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        logLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        logLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var logTitle = new Label
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            Font = new Font("Segoe UI", 14, FontStyle.Bold),
            ForeColor = TitleTextColor,
            Text = "Ablauf / Log"
        };

        var logSubtitle = new Label
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            ForeColor = MutedTextColor,
            Margin = new Padding(0, 4, 0, 12),
            Text = "Alle Schritte, Hinweise und Fehler erscheinen hier laufend."
        };

        _logBox.Dock = DockStyle.Fill;
        _logBox.ReadOnly = true;
        _logBox.Font = new Font("Consolas", 10);
        _logBox.BackColor = LogBackgroundColor;
        _logBox.ForeColor = LogTextColor;
        _logBox.BorderStyle = BorderStyle.None;

        logLayout.Controls.Add(logTitle, 0, 0);
        logLayout.Controls.Add(logSubtitle, 0, 1);
        logLayout.Controls.Add(_logBox, 0, 2);
        leftCard.Controls.Add(logLayout);

        var rightLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 15,
            AutoSize = false
        };
        rightLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        rightLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        rightLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        rightLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        rightLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        rightLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        rightLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        rightLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        rightLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        rightLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        rightLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 140));
        rightLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        rightLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        rightLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        rightLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        rightLayout.Padding = new Padding(4);
        rightLayout.Margin = new Padding(0);

        rightCard.Controls.Add(rightLayout);

        var titleLabel = new Label
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            Font = new Font("Segoe UI", 18, FontStyle.Bold),
            ForeColor = TitleTextColor,
            Margin = new Padding(0, 0, 0, 2),
            Text = "RDP-Signierer"
        };

        var infoLabel = new Label
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            ForeColor = MutedTextColor,
            Padding = new Padding(0, 4, 0, 10),
            Text = "RDP-Dateien mit lokalem Code-Signing-Zertifikat prüfen, bestätigen und signieren."
        };

        _environmentLabel.AutoSize = true;
        _environmentLabel.Dock = DockStyle.Top;
        _environmentLabel.BackColor = AccentSoftColor;
        _environmentLabel.ForeColor = AccentColor;
        _environmentLabel.BorderStyle = BorderStyle.FixedSingle;
        _environmentLabel.Padding = new Padding(12, 10, 12, 10);
        _environmentLabel.Margin = new Padding(0, 0, 0, 14);

        var warningPolicyTitle = CreateSectionLabel("Warnmeldung");
        var certificateTitle = CreateSectionLabel("Zertifikat");
        var filesTitle = CreateSectionLabel("RDP-Dateien");

        _warningPolicyStatusLabel.AutoSize = true;
        _warningPolicyStatusLabel.Dock = DockStyle.Top;
        _warningPolicyStatusLabel.BackColor = Color.FromArgb(248, 250, 253);
        _warningPolicyStatusLabel.BorderStyle = BorderStyle.FixedSingle;
        _warningPolicyStatusLabel.ForeColor = StatusNeutralColor;
        _warningPolicyStatusLabel.Padding = new Padding(12, 10, 12, 10);
        _warningPolicyStatusLabel.Margin = new Padding(0, 0, 0, 10);
        _warningPolicyStatusLabel.Text = "Registry-Status wird geladen...";

        _enableWarningSuppressionButton.AutoSize = true;
        _enableWarningSuppressionButton.Text = "Warnung unterdrücken";
        _enableWarningSuppressionButton.Click += (_, _) => SetWarningSuppression(enabled: true);
        StyleSecondaryButton(_enableWarningSuppressionButton);

        _disableWarningSuppressionButton.AutoSize = true;
        _disableWarningSuppressionButton.Text = "Rückgängig";
        _disableWarningSuppressionButton.Click += (_, _) => SetWarningSuppression(enabled: false);
        StyleSecondaryButton(_disableWarningSuppressionButton);

        var warningPolicyButtonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Margin = new Padding(0, 0, 0, 8)
        };

        warningPolicyButtonPanel.Controls.Add(_enableWarningSuppressionButton);
        warningPolicyButtonPanel.Controls.Add(_disableWarningSuppressionButton);

        var certificatePanel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 1,
            RowCount = 2,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 8)
        };
        certificatePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        certificatePanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        certificatePanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _certificateComboBox.Dock = DockStyle.Fill;
        _certificateComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _certificateComboBox.FlatStyle = FlatStyle.Flat;
        _certificateComboBox.Margin = new Padding(0);
        _certificateComboBox.SelectedIndexChanged += (_, _) => UpdateUiState();

        _refreshCertificatesButton.AutoSize = true;
        _refreshCertificatesButton.Text = "Neu laden";
        _refreshCertificatesButton.Click += (_, _) => RefreshCertificates();
        StyleSecondaryButton(_refreshCertificatesButton);

        _newCertificateButton.AutoSize = true;
        _newCertificateButton.Text = "Neues Zertifikat";
        _newCertificateButton.Click += (_, _) => CreateCertificate();
        StyleSecondaryButton(_newCertificateButton);

        _exportCertificateButton.AutoSize = true;
        _exportCertificateButton.Text = "Zertifikat exportieren";
        _exportCertificateButton.Click += (_, _) => ExportSelectedCertificate();
        StyleSecondaryButton(_exportCertificateButton);

        var certificateButtonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Margin = new Padding(0, 8, 0, 0)
        };

        certificateButtonPanel.Controls.Add(_refreshCertificatesButton);
        certificateButtonPanel.Controls.Add(_newCertificateButton);
        certificateButtonPanel.Controls.Add(_exportCertificateButton);

        certificatePanel.Controls.Add(_certificateComboBox, 0, 0);
        certificatePanel.Controls.Add(certificateButtonPanel, 0, 1);

        _fileStatusLabel.AutoSize = true;
        _fileStatusLabel.Dock = DockStyle.Top;
        _fileStatusLabel.BackColor = Color.FromArgb(248, 250, 253);
        _fileStatusLabel.BorderStyle = BorderStyle.FixedSingle;
        _fileStatusLabel.ForeColor = StatusNeutralColor;
        _fileStatusLabel.Padding = new Padding(12, 10, 12, 10);
        _fileStatusLabel.Margin = new Padding(0, 0, 0, 10);
        _fileStatusLabel.Text = "Keine Datei ausgewählt.";

        _dropZonePanel.Dock = DockStyle.Fill;
        _dropZonePanel.BorderStyle = BorderStyle.FixedSingle;
        _dropZonePanel.BackColor = AccentSoftColor;
        _dropZonePanel.Margin = new Padding(0, 0, 0, 10);
        _dropZonePanel.Padding = new Padding(0);
        WireDropTarget(_dropZonePanel);

        _dropZoneLabel.Dock = DockStyle.Fill;
        _dropZoneLabel.TextAlign = ContentAlignment.MiddleCenter;
        _dropZoneLabel.Font = new Font("Segoe UI", 11, FontStyle.Bold);
        _dropZoneLabel.Text = "RDP-Datei hier hineinziehen\noder unten über 'Durchsuchen' auswählen\n\nHinweis: Wenn die App als Administrator läuft, blockiert Windows Drag-and-Drop aus dem normalen Explorer.";
        _dropZoneLabel.AllowDrop = false;
        _dropZoneLabel.BackColor = AccentSoftColor;
        _dropZoneLabel.ForeColor = AccentColor;
        _dropZoneLabel.Padding = new Padding(18);
        _dropZoneLabel.Margin = new Padding(0);
        _dropZonePanel.Controls.Add(_dropZoneLabel);

        _fileListBox.Dock = DockStyle.Fill;
        _fileListBox.IntegralHeight = false;
        _fileListBox.SelectionMode = SelectionMode.MultiExtended;
        _fileListBox.HorizontalScrollbar = true;
        _fileListBox.BorderStyle = BorderStyle.FixedSingle;
        _fileListBox.BackColor = Color.FromArgb(249, 251, 254);
        _fileListBox.ForeColor = TitleTextColor;
        _fileListBox.Margin = new Padding(0, 0, 0, 10);
        WireDropTarget(_fileListBox);
        _fileListBox.SelectedIndexChanged += (_, _) =>
        {
            UpdateUiState();
            RefreshSelectedFileStatus();
        };

        var fileButtonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true
        };

        _browseButton.AutoSize = true;
        _browseButton.Text = "Durchsuchen";
        _browseButton.Click += (_, _) => BrowseFiles();
        StyleSecondaryButton(_browseButton);

        _removeButton.AutoSize = true;
        _removeButton.Text = "Auswahl entfernen";
        _removeButton.Click += (_, _) => RemoveSelectedFiles();
        StyleSecondaryButton(_removeButton);

        _clearButton.AutoSize = true;
        _clearButton.Text = "Alle entfernen";
        _clearButton.Click += (_, _) => ClearFiles();
        StyleSecondaryButton(_clearButton);

        fileButtonPanel.Controls.Add(_browseButton);
        fileButtonPanel.Controls.Add(_removeButton);
        fileButtonPanel.Controls.Add(_clearButton);

        _signButton.Dock = DockStyle.Top;
        _signButton.Height = 46;
        _signButton.Font = new Font("Segoe UI", 10, FontStyle.Bold);
        _signButton.Text = "Prüfen und signieren";
        _signButton.Click += async (_, _) => await SignSelectedFilesAsync();
        StylePrimaryButton(_signButton);

        var footnoteLabel = new Label
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            ForeColor = MutedTextColor,
            Padding = new Padding(0, 10, 0, 0),
            Text = "Hinweis: rdpsign überschreibt die ausgewählten .rdp-Dateien direkt."
        };

        rightLayout.Controls.Add(titleLabel, 0, 0);
        rightLayout.Controls.Add(infoLabel, 0, 1);
        rightLayout.Controls.Add(_environmentLabel, 0, 2);
        rightLayout.Controls.Add(warningPolicyTitle, 0, 3);
        rightLayout.Controls.Add(_warningPolicyStatusLabel, 0, 4);
        rightLayout.Controls.Add(warningPolicyButtonPanel, 0, 5);
        rightLayout.Controls.Add(certificateTitle, 0, 6);
        rightLayout.Controls.Add(certificatePanel, 0, 7);
        rightLayout.Controls.Add(filesTitle, 0, 8);
        rightLayout.Controls.Add(_fileStatusLabel, 0, 9);
        rightLayout.Controls.Add(_dropZonePanel, 0, 10);
        rightLayout.Controls.Add(_fileListBox, 0, 11);
        rightLayout.Controls.Add(fileButtonPanel, 0, 12);
        rightLayout.Controls.Add(_signButton, 0, 13);
        rightLayout.Controls.Add(footnoteLabel, 0, 14);
    }

    private void InitializeApplication()
    {
        AdjustSplitterDistance();
        AppendLog(LogLevel.Info, "Anwendung gestartet.");

        _config = _service.LoadConfig();
        _rdpSignPath = _service.GetRdpSignPath();
        _hashMode = _rdpSignPath is null ? "sha1" : _service.GetRdpSignHashMode(_rdpSignPath);

        if (_rdpSignPath is null)
        {
            _environmentLabel.Text = "rdpsign.exe wurde nicht gefunden.";
            AppendLog(LogLevel.Error, "rdpsign.exe wurde nicht gefunden. Prüfe die Windows-Komponenten.");
        }
        else
        {
            _environmentLabel.Text = $"rdpsign: {_rdpSignPath} | Hashmodus: /{_hashMode}";
            AppendLog(LogLevel.Success, $"rdpsign.exe gefunden: {_rdpSignPath}");
            AppendLog(LogLevel.Info, $"Verwendeter Hash-Parameter: /{_hashMode}");
        }

        RefreshCertificates();
        RefreshWarningPolicyStatus();
        RefreshSelectedFileStatus();
        AppendLog(LogLevel.Info, "Hinweis: Drag-and-Drop funktioniert unter Windows nicht aus dem normalen Explorer in ein erhöht gestartetes Programm.");
        UpdateUiState();
    }

    private void AdjustSplitterDistance()
    {
        _mainSplitContainer.Panel1MinSize = LeftPanelMinWidth;
        _mainSplitContainer.Panel2MinSize = RightPanelMinWidth;

        var maxLeftWidth = _mainSplitContainer.Width - RightPanelMinWidth;
        if (maxLeftWidth <= LeftPanelMinWidth)
        {
            return;
        }

        var preferredLeftWidth = Math.Min(620, maxLeftWidth);
        _mainSplitContainer.SplitterDistance = Math.Max(LeftPanelMinWidth, preferredLeftWidth);
    }

    private void RefreshCertificates()
    {
        _certificateComboBox.BeginUpdate();
        _certificateComboBox.Items.Clear();

        foreach (var certificate in _service.GetExistingCodeSigningCertificates())
        {
            _certificateComboBox.Items.Add(certificate);
        }

        _certificateComboBox.EndUpdate();

        if (_certificateComboBox.Items.Count == 0)
        {
            AppendLog(LogLevel.Warning, "Keine verwendbaren Code-Signing-Zertifikate gefunden.");
        }
        else
        {
            AppendLog(LogLevel.Info, $"{_certificateComboBox.Items.Count} verwendbare Code-Signing-Zertifikate gefunden.");
        }

        var selected = TrySelectConfiguredCertificate();
        if (!selected && _certificateComboBox.Items.Count > 0)
        {
            _certificateComboBox.SelectedIndex = 0;
        }

        UpdateUiState();
    }

    private void RefreshWarningPolicyStatus()
    {
        try
        {
            var state = _service.GetRedirectionWarningPolicyState();
            if (state.WarningSuppressed)
            {
                _warningPolicyStatusLabel.Text = "Warnmeldung unterdrückt. 'RedirectionWarningDialogVersion' ist auf 1 gesetzt.";
                _warningPolicyStatusLabel.ForeColor = SuccessColor;
                _warningPolicyStatusLabel.BackColor = Color.FromArgb(241, 250, 244);
                return;
            }

            if (state.ValueExists)
            {
                _warningPolicyStatusLabel.Text = $"Registry-Wert vorhanden, aber nicht aktiv für die Unterdrückung. Aktueller Wert: {state.Value}.";
                _warningPolicyStatusLabel.ForeColor = WarningColor;
                _warningPolicyStatusLabel.BackColor = Color.FromArgb(255, 249, 238);
                return;
            }

            _warningPolicyStatusLabel.Text = "Warnmeldung aktiv. Der Registry-Wert ist derzeit nicht gesetzt.";
            _warningPolicyStatusLabel.ForeColor = StatusNeutralColor;
            _warningPolicyStatusLabel.BackColor = Color.FromArgb(248, 250, 253);
        }
        catch (Exception ex)
        {
            _warningPolicyStatusLabel.Text = $"Registry-Status konnte nicht gelesen werden: {ex.Message}";
            _warningPolicyStatusLabel.ForeColor = ErrorColor;
            _warningPolicyStatusLabel.BackColor = Color.FromArgb(255, 245, 245);
        }
    }

    private void SetWarningSuppression(bool enabled)
    {
        try
        {
            using var process = StartElevatedRegistryAction(enabled ? "enable" : "disable");
            process.WaitForExit();

            if (process.ExitCode == 0)
            {
                if (enabled)
                {
                    AppendLog(LogLevel.Success, "Registry-Wert gesetzt: HKLM\\Software\\Policies\\Microsoft\\Windows NT\\Terminal Services\\Client\\RedirectionWarningDialogVersion = 1");
                }
                else
                {
                    AppendLog(LogLevel.Success, "Registry-Wert für die Warnungsunterdrückung entfernt.");
                }
            }
            else
            {
                AppendLog(LogLevel.Warning, "Die Registry-Aktion wurde nicht erfolgreich abgeschlossen.");
            }

            RefreshWarningPolicyStatus();
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            const string message = "Die Administrator-Bestätigung wurde abgebrochen.";
            AppendLog(LogLevel.Warning, message);
            MessageBox.Show(this, message, "Aktion abgebrochen", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            AppendLog(LogLevel.Error, ex.Message);
            MessageBox.Show(this, ex.Message, "Registry konnte nicht geändert werden", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static Process StartElevatedRegistryAction(string action)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = Application.ExecutablePath,
            Arguments = $"--registry-warning-suppression {action}",
            UseShellExecute = true,
            Verb = "runas"
        };

        return Process.Start(startInfo)
            ?? throw new InvalidOperationException("Die administrative Hilfsinstanz konnte nicht gestartet werden.");
    }

    private void ExportSelectedCertificate()
    {
        if (_certificateComboBox.SelectedItem is not RdpSigningService.CertificateChoice selectedChoice)
        {
            MessageBox.Show(this, "Bitte zuerst ein Zertifikat auswählen.", "Kein Zertifikat", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        using var certificate = _service.GetCertificateByThumbprint(selectedChoice.Thumbprint);
        if (certificate is null)
        {
            MessageBox.Show(this, "Das ausgewählte Zertifikat wurde nicht gefunden.", "Zertifikat fehlt", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        var defaultName = BuildSafeFileName($"{certificate.Subject}_{certificate.Thumbprint}.cer");
        using var dialog = new SaveFileDialog
        {
            Title = "Zertifikat exportieren",
            Filter = "Zertifikat (*.cer)|*.cer",
            FileName = defaultName,
            OverwritePrompt = true
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            _service.ExportCertificate(certificate, dialog.FileName);
            AppendLog(LogLevel.Success, $"Zertifikat exportiert: {dialog.FileName}");
            MessageBox.Show(this, "Das Zertifikat wurde exportiert.", "Export erfolgreich", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            AppendLog(LogLevel.Error, ex.Message);
            MessageBox.Show(this, ex.Message, "Export fehlgeschlagen", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void RefreshSelectedFileStatus()
    {
        var file = _fileListBox.SelectedItem as string ??
            _fileListBox.Items.Cast<string>().FirstOrDefault();

        if (string.IsNullOrWhiteSpace(file))
        {
            SetFileStatus("Keine Datei ausgewählt.", StatusNeutralColor, Color.FromArgb(248, 250, 253));
            return;
        }

        if (!File.Exists(file))
        {
            SetFileStatus($"Datei nicht gefunden: {Path.GetFileName(file)}", ErrorColor, Color.FromArgb(255, 245, 245));
            return;
        }

        try
        {
            var inspection = _service.InspectRdpFile(file);
            if (inspection.HasSignatureField)
            {
                var signScopeText = inspection.HasSignScopeField ? "signature:s und signscope:s vorhanden" : "signature:s vorhanden";
                SetFileStatus(
                    $"{Path.GetFileName(file)} ist signiert. {signScopeText}.",
                    SuccessColor,
                    Color.FromArgb(241, 250, 244));
                return;
            }

            SetFileStatus(
                $"{Path.GetFileName(file)} ist aktuell nicht signiert.",
                WarningColor,
                Color.FromArgb(255, 249, 238));
        }
        catch (Exception ex)
        {
            SetFileStatus(
                $"Signaturstatus konnte nicht gelesen werden: {ex.Message}",
                ErrorColor,
                Color.FromArgb(255, 245, 245));
        }
    }

    private bool TrySelectConfiguredCertificate()
    {
        if (string.IsNullOrWhiteSpace(_config.Thumbprint))
        {
            return false;
        }

        for (var i = 0; i < _certificateComboBox.Items.Count; i++)
        {
            if (_certificateComboBox.Items[i] is not RdpSigningService.CertificateChoice choice)
            {
                continue;
            }

            if (string.Equals(choice.Thumbprint, _config.Thumbprint, StringComparison.OrdinalIgnoreCase))
            {
                _certificateComboBox.SelectedIndex = i;
                AppendLog(LogLevel.Info, $"Gespeichertes Zertifikat vorausgewählt: {choice.Subject}");
                return true;
            }
        }

        return false;
    }

    private void CreateCertificate()
    {
        using var dialog = new SubjectPromptDialog(_config.Subject);
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            SetBusy(true);
            AppendLog(LogLevel.Info, $"Erzeuge neues selbstsigniertes Zertifikat: {dialog.SubjectName}");
            using var certificate = _service.CreateSelfSignedCodeSigningCertificate(dialog.SubjectName);
            AppendLog(LogLevel.Success, $"Zertifikat erstellt: {certificate.Subject}");
            _config = new RdpSigningService.AppConfig(certificate.Thumbprint, certificate.Subject, DateTimeOffset.Now.ToString("s"));
            RefreshCertificates();
        }
        catch (Exception ex)
        {
            AppendLog(LogLevel.Error, ex.Message);
            MessageBox.Show(this, ex.Message, "Zertifikat konnte nicht erstellt werden", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void BrowseFiles()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Eine oder mehrere RDP-Dateien auswählen",
            Filter = "RDP-Dateien (*.rdp)|*.rdp|Alle Dateien (*.*)|*.*",
            Multiselect = true
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            AddFiles(dialog.FileNames);
        }
    }

    private void WireDropTarget(Control control)
    {
        control.AllowDrop = true;
        control.DragEnter += HandleDragEnter;
        control.DragDrop += HandleDragDrop;
    }

    private void HandleDragEnter(object? sender, DragEventArgs e)
    {
        if (HasRdpFiles(e.Data))
        {
            e.Effect = DragDropEffects.Copy;
            return;
        }

        e.Effect = DragDropEffects.None;
    }

    private void HandleDragDrop(object? sender, DragEventArgs e)
    {
        if (!e.Data!.GetDataPresent(DataFormats.FileDrop))
        {
            return;
        }

        if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
        {
            AddFiles(files);
        }
    }

    private static bool HasRdpFiles(IDataObject? dataObject)
    {
        if (dataObject?.GetData(DataFormats.FileDrop) is not string[] files)
        {
            return false;
        }

        return files.Any(file => string.Equals(Path.GetExtension(file), ".rdp", StringComparison.OrdinalIgnoreCase));
    }

    private void AddFiles(IEnumerable<string> files)
    {
        var addedCount = 0;
        foreach (var file in files)
        {
            if (!File.Exists(file))
            {
                continue;
            }

            if (!string.Equals(Path.GetExtension(file), ".rdp", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (_selectedFiles.Add(file))
            {
                _fileListBox.Items.Add(file);
                addedCount++;
            }
        }

        if (addedCount > 0)
        {
            AppendLog(LogLevel.Info, $"{addedCount} Datei(en) zur Signierliste hinzugefügt.");
        }
        else
        {
            AppendLog(LogLevel.Warning, "Keine neuen .rdp-Dateien hinzugefügt.");
        }

        RefreshSelectedFileStatus();
        UpdateUiState();
    }

    private void RemoveSelectedFiles()
    {
        var selectedItems = _fileListBox.SelectedItems.Cast<string>().ToList();
        if (selectedItems.Count == 0)
        {
            return;
        }

        foreach (var item in selectedItems)
        {
            _selectedFiles.Remove(item);
            _fileListBox.Items.Remove(item);
        }

        AppendLog(LogLevel.Info, $"{selectedItems.Count} Datei(en) entfernt.");
        RefreshSelectedFileStatus();
        UpdateUiState();
    }

    private void ClearFiles()
    {
        if (_fileListBox.Items.Count == 0)
        {
            return;
        }

        _selectedFiles.Clear();
        _fileListBox.Items.Clear();
        AppendLog(LogLevel.Info, "Dateiliste geleert.");
        RefreshSelectedFileStatus();
        UpdateUiState();
    }

    private async Task SignSelectedFilesAsync()
    {
        if (_isBusy)
        {
            return;
        }

        if (_rdpSignPath is null)
        {
            MessageBox.Show(this, "rdpsign.exe wurde nicht gefunden.", "Fehlende Voraussetzung", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        if (_certificateComboBox.SelectedItem is not RdpSigningService.CertificateChoice selectedChoice)
        {
            MessageBox.Show(this, "Bitte zuerst ein Zertifikat auswählen oder erstellen.", "Kein Zertifikat", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (_selectedFiles.Count == 0)
        {
            MessageBox.Show(this, "Bitte mindestens eine .rdp-Datei auswählen.", "Keine Datei ausgewählt", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        X509Certificate2? certificate = null;

        try
        {
            SetBusy(true);
            certificate = _service.GetCertificateByThumbprint(selectedChoice.Thumbprint);

            if (certificate is null)
            {
                throw new InvalidOperationException("Das ausgewählte Zertifikat wurde nicht mehr gefunden.");
            }

            AppendLog(LogLevel.Info, $"Verwende Zertifikat: {certificate.Subject}");
            _service.EnsureCertificateTrust(certificate, AppendLog);
            _service.SaveConfig(certificate.Thumbprint, certificate.Subject);
            AppendLog(LogLevel.Success, $"Konfiguration gespeichert: {_service.ConfigPath}");

            var files = _selectedFiles.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList();

            AppendLog(LogLevel.Info, "Starte Testlauf mit rdpsign /l ...");
            await _service.RunRdpsignAsync(_rdpSignPath, _hashMode, certificate.Thumbprint, files, testOnly: true, AppendLog);

            var confirmResult = MessageBox.Show(
                this,
                "Der Testlauf war erfolgreich. Sollen die ausgewählten Dateien jetzt wirklich signiert werden?",
                "Signieren bestätigen",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (confirmResult != DialogResult.Yes)
            {
                AppendLog(LogLevel.Warning, "Abgebrochen. Es wurden keine Dateien verändert.");
                return;
            }

            AppendLog(LogLevel.Info, "Signiere Dateien ...");
            await _service.RunRdpsignAsync(_rdpSignPath, _hashMode, certificate.Thumbprint, files, testOnly: false, AppendLog);

            AppendLog(LogLevel.Success, "Fertig.");
            AppendLog(LogLevel.Info, "Hinweis: Bei .rdp-Dateien ist der Windows-Explorer kein verlässlicher Signaturnachweis. Maßgeblich sind die Signaturfelder in der Datei und der Publisher-Hinweis beim Öffnen der RDP-Datei.");
            RefreshSelectedFileStatus();
            MessageBox.Show(this, "Die ausgewählten RDP-Dateien wurden signiert.", "Erfolgreich", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            AppendLog(LogLevel.Error, ex.Message);
            MessageBox.Show(this, ex.Message, "Fehler beim Signieren", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            certificate?.Dispose();
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        _isBusy = busy;
        UpdateUiState();
    }

    private static Panel CreateCardPanel()
    {
        return new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = CardBackgroundColor,
            BorderStyle = BorderStyle.FixedSingle,
            Padding = new Padding(18)
        };
    }

    private void SetFileStatus(string text, Color foregroundColor, Color backgroundColor)
    {
        _fileStatusLabel.Text = text;
        _fileStatusLabel.ForeColor = foregroundColor;
        _fileStatusLabel.BackColor = backgroundColor;
    }

    private static string BuildSafeFileName(string value)
    {
        var sanitized = value;
        foreach (var invalidCharacter in Path.GetInvalidFileNameChars())
        {
            sanitized = sanitized.Replace(invalidCharacter, '_');
        }

        return sanitized;
    }

    private static Label CreateSectionLabel(string text)
    {
        return new Label
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            Font = new Font("Segoe UI", 10, FontStyle.Bold),
            ForeColor = TitleTextColor,
            Margin = new Padding(0, 6, 0, 8),
            Text = text
        };
    }

    private static void StyleSecondaryButton(Button button)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderColor = BorderColor;
        button.FlatAppearance.MouseDownBackColor = Color.FromArgb(237, 237, 237);
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(243, 243, 243);
        button.BackColor = Color.FromArgb(249, 249, 249);
        button.ForeColor = TitleTextColor;
        button.Padding = new Padding(10, 6, 10, 6);
        button.Margin = new Padding(0, 0, 8, 0);
    }

    private static void StylePrimaryButton(Button button)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseDownBackColor = Color.FromArgb(18, 71, 162);
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(28, 100, 224);
        button.BackColor = AccentColor;
        button.ForeColor = Color.White;
    }

    private void UpdateUiState()
    {
        _certificateComboBox.Enabled = !_isBusy;
        _refreshCertificatesButton.Enabled = !_isBusy;
        _newCertificateButton.Enabled = !_isBusy;
        _exportCertificateButton.Enabled = !_isBusy && _certificateComboBox.SelectedItem is not null;
        _enableWarningSuppressionButton.Enabled = !_isBusy;
        _disableWarningSuppressionButton.Enabled = !_isBusy;
        _browseButton.Enabled = !_isBusy;
        _removeButton.Enabled = !_isBusy && _fileListBox.SelectedItems.Count > 0;
        _clearButton.Enabled = !_isBusy && _fileListBox.Items.Count > 0;
        _signButton.Enabled = !_isBusy &&
            _rdpSignPath is not null &&
            _certificateComboBox.SelectedItem is not null &&
            _fileListBox.Items.Count > 0;
        _signButton.BackColor = _isBusy ? SuccessColor : AccentColor;
        _signButton.Text = _isBusy ? "Arbeite ..." : "Prüfen und signieren";
    }

    private void AppendLog(LogLevel level, string message)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => AppendLog(level, message));
            return;
        }

        var color = level switch
        {
            LogLevel.Success => Color.FromArgb(113, 227, 145),
            LogLevel.Warning => Color.FromArgb(255, 214, 102),
            LogLevel.Error => Color.FromArgb(255, 145, 145),
            _ => LogTextColor
        };

        var prefix = level switch
        {
            LogLevel.Success => "[OK]   ",
            LogLevel.Warning => "[WARN] ",
            LogLevel.Error => "[ERR]  ",
            _ => "[INFO] "
        };

        _logBox.SelectionStart = _logBox.TextLength;
        _logBox.SelectionLength = 0;
        _logBox.SelectionColor = color;
        _logBox.AppendText($"{DateTime.Now:HH:mm:ss} {prefix}{message}{Environment.NewLine}");
        _logBox.SelectionColor = _logBox.ForeColor;
        _logBox.ScrollToCaret();
    }

    private sealed class SubjectPromptDialog : Form
    {
        private readonly TextBox _subjectTextBox = new();

        public string SubjectName => string.IsNullOrWhiteSpace(_subjectTextBox.Text)
            ? "CN=RDP File Signing"
            : _subjectTextBox.Text.Trim();

        public SubjectPromptDialog(string? initialValue)
        {
            Text = "Neues Zertifikat";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(460, 140);

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                Padding = new Padding(12)
            };
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var label = new Label
            {
                AutoSize = true,
                Text = "Betreff für das neue Zertifikat (leer = CN=RDP File Signing)"
            };

            _subjectTextBox.Dock = DockStyle.Top;
            _subjectTextBox.Text = initialValue ?? string.Empty;

            var buttonPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                AutoSize = true,
                WrapContents = false
            };

            var okButton = new Button
            {
                AutoSize = true,
                Text = "OK",
                DialogResult = DialogResult.OK
            };

            var cancelButton = new Button
            {
                AutoSize = true,
                Text = "Abbrechen",
                DialogResult = DialogResult.Cancel
            };

            buttonPanel.Controls.Add(okButton);
            buttonPanel.Controls.Add(cancelButton);

            AcceptButton = okButton;
            CancelButton = cancelButton;

            layout.Controls.Add(label, 0, 0);
            layout.Controls.Add(_subjectTextBox, 0, 1);
            layout.Controls.Add(new Panel(), 0, 2);
            layout.Controls.Add(buttonPanel, 0, 3);

            Controls.Add(layout);
        }
    }
}

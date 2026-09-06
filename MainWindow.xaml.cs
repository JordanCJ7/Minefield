using System;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using SkiaSharp.Views.WPF;
using Minefield.Engine;
using Minefield.Rendering;

namespace Minefield;

public enum ViewMode
{
    MainMenu,
    InGame,
    Achievements,
    Profile,
    Themes,
    Intel,
    Pause,
    NewGameModal,
    GameOverModal
}

/// <summary>
/// Main Game Window hosting the SkiaSharp immediate-mode rendering engine,
/// particle physics system, synthesized audio, multi-view navigation, and glassmorphism HUD.
/// </summary>
public partial class MainWindow : Window
{
    private readonly Camera _camera = new();
    private readonly InfiniteGridRenderer _gridRenderer = new();
    private readonly ChunkManager _chunkManager = new();
    private readonly PlayerProfile _profile = new();
    private readonly ParticleSystem _particles = new();
    private readonly SynthesizedAudio _audio = new();
    private readonly GameSession _gameSession;

    // View Navigation State
    private ViewMode _currentView = ViewMode.MainMenu;
    private ViewMode _previousView = ViewMode.MainMenu;
    private ViewMode _modalReturnView = ViewMode.MainMenu;
    private string _currentAchievementFilter = "All";
    private readonly DispatcherTimer _toastTimer = new();
    private readonly DispatcherTimer _shatterTimer = new();

    // Mouse Navigation & Gameplay State
    private bool _isPanning;
    private Point _lastMousePosition;
    private Point _mouseDownPos;
    private bool _hasDragged;
    private SKPoint? _lastMouseScreenPixel;

    // Frame Timing & Ambient Simulation
    private readonly Stopwatch _fpsStopwatch = Stopwatch.StartNew();
    private readonly Stopwatch _deltaStopwatch = Stopwatch.StartNew();
    private int _frameCount;
    private double _lastFpsUpdate;
    private double _currentFps = 60.0;
    private float _ambientTime = 0.0f;

    public MainWindow()
    {
        InitializeComponent();

        _gameSession = new GameSession(_chunkManager, _profile, _particles, _audio);
        _gameSession.OnSectorLocked += GameSession_OnSectorLocked;
        _gameSession.OnMineDetonated += GameSession_OnMineDetonated;
        _gameSession.OnDetonationTrauma += GameSession_OnDetonationTrauma;

        _profile.OnProfileChanged += UpdateHudStats;
        _profile.OnStatusMessage += msg => Dispatcher.Invoke(() => TxtSectorStatus.Text = msg);
        _profile.Achievements.OnAchievementUnlocked += ShowAchievementToast;
        _profile.OnComboShattered += Profile_OnComboShattered;
        _profile.OnEnergyDepleted += Profile_OnEnergyDepleted;

        ThemeManager.OnThemeChanged += OnThemeApplied;

        // Toast timer setup (auto-fades after 4 seconds)
        _toastTimer.Interval = TimeSpan.FromSeconds(4.0);
        _toastTimer.Tick += (s, e) =>
        {
            _toastTimer.Stop();
            AchievementToastBorder.Visibility = Visibility.Collapsed;
        };

        // Combo shatter alert banner timer (auto-fades after 2.5 seconds)
        _shatterTimer.Interval = TimeSpan.FromSeconds(2.5);
        _shatterTimer.Tick += (s, e) =>
        {
            _shatterTimer.Stop();
            BannerComboShatter.Visibility = Visibility.Collapsed;
        };

        // Load existing player profile from SQLite
        _profile.LoadFromDatabase();

        // Apply saved theme
        ThemeManager.SetTheme(_profile.ActiveThemeId, Application.Current.Resources);
        _gridRenderer.ApplyTheme(ThemeManager.CurrentTheme);

        // Continuous 60+ FPS rendering loop
        CompositionTarget.Rendering += OnCompositionRendering;

        KeyDown += MainWindow_KeyDown;
        Loaded += MainWindow_Loaded;
        Closed += MainWindow_Closed;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateTelemetry(0, 0);
        UpdateHudStats();
        UpdateMainMenuSummary();
        UpdateThemeBadges();
        SwitchView(ViewMode.MainMenu, playSound: false);
    }

    private async void MainWindow_Closed(object? sender, EventArgs e)
    {
        CompositionTarget.Rendering -= OnCompositionRendering;
        _profile.SaveToDatabase();
        await _chunkManager.DisposeAsync();
        _particles.Dispose();
        _audio.Dispose();
        _gridRenderer.Dispose();
    }

    #region View Management & Navigation

    public void SwitchView(ViewMode mode, bool playSound = true)
    {
        if (playSound)
        {
            _audio.PlayMenuClick();
        }

        _previousView = _currentView;
        _currentView = mode;

        // Reset visibility on all overlay views
        MainMenuPanel.Visibility = (mode == ViewMode.MainMenu) ? Visibility.Visible : Visibility.Collapsed;
        InGameHudGrid.Visibility = (mode == ViewMode.InGame || mode == ViewMode.Pause) ? Visibility.Visible : Visibility.Collapsed;
        PauseOverlayGrid.Visibility = (mode == ViewMode.Pause) ? Visibility.Visible : Visibility.Collapsed;
        AchievementsPanel.Visibility = (mode == ViewMode.Achievements) ? Visibility.Visible : Visibility.Collapsed;
        ProfilePanel.Visibility = (mode == ViewMode.Profile) ? Visibility.Visible : Visibility.Collapsed;
        ThemesPanel.Visibility = (mode == ViewMode.Themes) ? Visibility.Visible : Visibility.Collapsed;
        IntelPanel.Visibility = (mode == ViewMode.Intel) ? Visibility.Visible : Visibility.Collapsed;
        NewGameModalGrid.Visibility = (mode == ViewMode.NewGameModal) ? Visibility.Visible : Visibility.Collapsed;
        GameOverModalGrid.Visibility = (mode == ViewMode.GameOverModal) ? Visibility.Visible : Visibility.Collapsed;

        // Update title bar breadcrumb badge
        TxtViewBreadcrumb.Text = mode switch
        {
            ViewMode.MainMenu => "MAIN MENU",
            ViewMode.InGame => "TACTICAL EXPLORATION",
            ViewMode.Pause => "MISSION PAUSED",
            ViewMode.Achievements => "ACHIEVEMENTS ARCHIVE",
            ViewMode.Profile => "OPERATIVE DOSSIER",
            ViewMode.Themes => "THEME MATRIX",
            ViewMode.Intel => "TACTICAL INTEL",
            ViewMode.NewGameModal => "EXPEDITION CONFIG",
            ViewMode.GameOverModal => "CRITICAL FAILURE",
            _ => "SYSTEM"
        };

        // Refresh dynamic screen content
        if (mode == ViewMode.MainMenu)
        {
            UpdateMainMenuSummary();
        }
        else if (mode == ViewMode.Achievements)
        {
            PopulateAchievements();
        }
        else if (mode == ViewMode.Profile)
        {
            PopulateProfile();
        }
        else if (mode == ViewMode.Themes)
        {
            UpdateThemeBadges();
        }
    }

    private void UpdateMainMenuSummary()
    {
        TxtMenuRank.Text = $"LVL {_profile.Level:D2} [{_profile.RankTitle}]";
        TxtMenuEnergy.Text = $"{_profile.CurrentEnergy} / {_profile.MaxEnergy}";
        TxtMenuTheme.Text = ThemeManager.CurrentTheme.Name;
        TxtMenuAchievementsCount.Text = $"{_profile.Achievements.UnlockedCount}/{_profile.Achievements.TotalCount}";
    }

    private void BtnPlayGame_Click(object sender, RoutedEventArgs e)
    {
        BtnContinueGame_Click(sender, e);
    }

    private void BtnContinueGame_Click(object sender, RoutedEventArgs e)
    {
        SwitchView(ViewMode.InGame);
        TxtSectorStatus.Text = "STATUS: EXPLORATION ACTIVE // ZERO GUESS DETECTOR RUNNING";
    }

    private void BtnNewGameModal_Click(object sender, RoutedEventArgs e)
    {
        _modalReturnView = (_currentView == ViewMode.Pause) ? ViewMode.Pause : ViewMode.MainMenu;
        SelectThreatCard(_profile.CurrentDifficulty);
        SwitchView(ViewMode.NewGameModal);
    }

    private void ThreatCard_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border border && border.Tag is string tag && Enum.TryParse<DifficultyLevel>(tag, out var level))
        {
            SelectThreatCard(level);
            _audio.PlayMenuClick();
        }
    }

    private void SelectThreatCard(DifficultyLevel level)
    {
        _profile.CurrentDifficulty = level;

        RbThreatCadet.IsChecked = (level == DifficultyLevel.Cadet);
        RbThreatStandard.IsChecked = (level == DifficultyLevel.Standard);
        RbThreatHazard.IsChecked = (level == DifficultyLevel.Hazard);
        RbThreatNightmare.IsChecked = (level == DifficultyLevel.Nightmare);

        CardThreatCadet.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(level == DifficultyLevel.Cadet ? "#2600E676" : "#141E34"));
        CardThreatCadet.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(level == DifficultyLevel.Cadet ? "#00E676" : "#3300E676"));

        CardThreatStandard.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(level == DifficultyLevel.Standard ? "#2600E5FF" : "#141E34"));
        CardThreatStandard.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(level == DifficultyLevel.Standard ? "#00E5FF" : "#3300E5FF"));

        CardThreatHazard.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(level == DifficultyLevel.Hazard ? "#26FFA726" : "#141E34"));
        CardThreatHazard.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(level == DifficultyLevel.Hazard ? "#FFA726" : "#33FFA726"));

        CardThreatNightmare.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(level == DifficultyLevel.Nightmare ? "#26FF1744" : "#141E34"));
        CardThreatNightmare.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(level == DifficultyLevel.Nightmare ? "#FF1744" : "#33FF1744"));
    }

    private void BtnRandomizeSeed_Click(object sender, RoutedEventArgs e)
    {
        TxtSeedInput.Text = Random.Shared.Next(1000, 99999).ToString();
        _audio.PlayMenuClick();
    }

    private void BtnCancelNewGame_Click(object sender, RoutedEventArgs e)
    {
        SwitchView(_modalReturnView);
    }

    private async void BtnLaunchNewExpedition_Click(object sender, RoutedEventArgs e)
    {
        _audio.PlaySectorLock();

        if (!int.TryParse(TxtSeedInput.Text, out int seed))
        {
            seed = 1337;
        }

        var config = DifficultyConfig.GetConfig(_profile.CurrentDifficulty);

        await _chunkManager.ResetAllChunksAsync(seed, config.MineDensityPercent);
        _profile.RebootExpedition(config.StartingShields, config.StartingDrones);

        _camera.Reset();
        UpdateTelemetry(0, 0);

        SwitchView(ViewMode.InGame);
        TxtSectorStatus.Text = $"STATUS: NEW EXPEDITION INITIALIZED // THREAT: {config.Level.ToString().ToUpper()} // SEED: {seed}";
    }

    private async void BtnRebootExpedition_Click(object sender, RoutedEventArgs e)
    {
        _audio.PlaySectorLock();
        var config = DifficultyConfig.GetConfig(_profile.CurrentDifficulty);

        int newSeed = Random.Shared.Next(1000, 99999);
        await _chunkManager.ResetAllChunksAsync(newSeed, config.MineDensityPercent);
        _profile.RebootExpedition(config.StartingShields, config.StartingDrones);

        _camera.Reset();
        UpdateTelemetry(0, 0);

        SwitchView(ViewMode.InGame);
        TxtSectorStatus.Text = "STATUS: SYSTEM REBOOTED // EMERGENCY RESPAWN AT [0, 0]";
    }

    private void BtnGameOverMenu_Click(object sender, RoutedEventArgs e)
    {
        _profile.SaveToDatabase();
        SwitchView(ViewMode.MainMenu);
    }

    private void BtnOpenAchievements_Click(object sender, RoutedEventArgs e)
    {
        SwitchView(ViewMode.Achievements);
    }

    private void BtnOpenProfile_Click(object sender, RoutedEventArgs e)
    {
        SwitchView(ViewMode.Profile);
    }

    private void BtnOpenThemes_Click(object sender, RoutedEventArgs e)
    {
        SwitchView(ViewMode.Themes);
    }

    private void BtnOpenIntel_Click(object sender, RoutedEventArgs e)
    {
        SwitchView(ViewMode.Intel);
    }

    private void BtnReturnToMenu_Click(object sender, RoutedEventArgs e)
    {
        SwitchView(ViewMode.MainMenu);
    }

    private void BtnPauseMenu_Click(object sender, RoutedEventArgs e)
    {
        SwitchView(ViewMode.Pause);
    }

    private void BtnResumeGame_Click(object sender, RoutedEventArgs e)
    {
        SwitchView(ViewMode.InGame);
    }

    private void BtnPauseMainMenu_Click(object sender, RoutedEventArgs e)
    {
        _profile.SaveToDatabase();
        SwitchView(ViewMode.MainMenu);
    }

    private void BtnExitGame_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    #endregion

    #region Achievements Section Logic

    private void PopulateAchievements()
    {
        AchievementsContainer.Children.Clear();

        var list = _profile.Achievements.Achievements;
        int unlockedCount = _profile.Achievements.UnlockedCount;
        int totalCount = _profile.Achievements.TotalCount;
        int percent = totalCount > 0 ? (int)((float)unlockedCount / totalCount * 100) : 0;

        TxtAchievementsStats.Text = $"{unlockedCount} / {totalCount} UNLOCKED ({percent}%)";
        TxtAchievementsXp.Text = $"+{_profile.Achievements.TotalXpEarnedFromAchievements:N0} BONUS XP";

        var filtered = _currentAchievementFilter switch
        {
            "Exploration" => list.Where(a => a.Category == AchievementCategory.Exploration),
            "Tactics" => list.Where(a => a.Category == AchievementCategory.Tactics),
            "Survival" => list.Where(a => a.Category == AchievementCategory.Survival),
            "Progression" => list.Where(a => a.Category == AchievementCategory.Progression),
            _ => list
        };

        foreach (var ach in filtered)
        {
            var card = CreateAchievementCard(ach);
            AchievementsContainer.Children.Add(card);
        }
    }

    private Border CreateAchievementCard(Achievement ach)
    {
        var cardBorder = new Border
        {
            Background = new SolidColorBrush(ach.IsUnlocked ? System.Windows.Media.Color.FromArgb(0x40, 0x00, 0xE6, 0x76) : System.Windows.Media.Color.FromArgb(0x20, 0x14, 0x20, 0x36)),
            BorderBrush = ach.IsUnlocked ? (Brush)FindResource("NeonEmeraldBrush") : (Brush)FindResource("BorderGlassBrush"),
            BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 10, 14, 10),
            Margin = new Thickness(0, 0, 0, 8)
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(46) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Column 0: Glyph Icon in Box
        var iconBorder = new Border
        {
            Width = 38,
            Height = 38,
            CornerRadius = new CornerRadius(6),
            Background = ach.IsUnlocked ? new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x33, 0x00, 0xE6, 0x76)) : new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x20, 0x00, 0xE5, 0xFF)),
            BorderBrush = ach.IsUnlocked ? (Brush)FindResource("NeonEmeraldBrush") : (Brush)FindResource("BorderGlassBrush"),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center
        };
        var iconText = new TextBlock
        {
            Text = ach.GlyphIcon,
            FontSize = 18,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        iconBorder.Child = iconText;
        Grid.SetColumn(iconBorder, 0);
        grid.Children.Add(iconBorder);

        // Column 1: Info and Progress
        var infoStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 12, 0) };
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal };
        var titleText = new TextBlock
        {
            Text = ach.Title.ToUpper(),
            FontFamily = new FontFamily("Consolas, Segoe UI"),
            FontWeight = FontWeights.Bold,
            FontSize = 12,
            Foreground = ach.IsUnlocked ? (Brush)FindResource("TextPrimaryBrush") : (Brush)FindResource("TextMutedBrush")
        };
        titleRow.Children.Add(titleText);

        var descText = new TextBlock
        {
            Text = ach.Description,
            FontFamily = new FontFamily("Consolas, Segoe UI"),
            FontSize = 10,
            Foreground = (Brush)FindResource("TextMutedBrush"),
            Margin = new Thickness(0, 2, 0, 4)
        };

        var progressGrid = new Grid();
        var progressBar = new ProgressBar
        {
            Height = 4,
            Maximum = ach.TargetValue,
            Value = Math.Min(ach.CurrentValue, ach.TargetValue),
            Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x30, 0x14, 0x20, 0x36)),
            Foreground = ach.IsUnlocked ? (Brush)FindResource("NeonEmeraldBrush") : (Brush)FindResource("NeonCyanBrush"),
            BorderThickness = new Thickness(0)
        };
        progressGrid.Children.Add(progressBar);

        var progressLabel = new TextBlock
        {
            Text = ach.IsUnlocked
                ? $"COMPLETED ({ach.CurrentValue:N0} / {ach.TargetValue:N0})"
                : $"PROGRESS: {ach.CurrentValue:N0} / {ach.TargetValue:N0} ({ach.ProgressPercent}%)",
            FontFamily = new FontFamily("Consolas"),
            FontSize = 9,
            Foreground = ach.IsUnlocked ? (Brush)FindResource("NeonEmeraldBrush") : (Brush)FindResource("NeonCyanBrush"),
            Margin = new Thickness(0, 3, 0, 0)
        };

        infoStack.Children.Add(titleRow);
        infoStack.Children.Add(descText);
        infoStack.Children.Add(progressGrid);
        infoStack.Children.Add(progressLabel);

        Grid.SetColumn(infoStack, 1);
        grid.Children.Add(infoStack);

        // Column 2: Reward and Status Badge
        var rightStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
        var rewardBadge = new Border
        {
            Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x20, 0xFF, 0xD6, 0x00)),
            BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x40, 0xFF, 0xD6, 0x00)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 3, 8, 3),
            Margin = new Thickness(0, 0, 0, 4)
        };
        var rewardText = new TextBlock
        {
            Text = $"+{ach.XpReward:N0} XP",
            FontFamily = new FontFamily("Consolas"),
            FontWeight = FontWeights.Bold,
            FontSize = 10,
            Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0xD6, 0x00))
        };
        rewardBadge.Child = rewardText;

        var statusBadge = new Border
        {
            Background = ach.IsUnlocked ? new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x20, 0x00, 0xE6, 0x76)) : new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x20, 0x00, 0xE5, 0xFF)),
            BorderBrush = ach.IsUnlocked ? (Brush)FindResource("NeonEmeraldBrush") : (Brush)FindResource("BorderGlassBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 3, 8, 3)
        };
        var statusText = new TextBlock
        {
            Text = ach.IsUnlocked ? "✓ UNLOCKED" : "LOCKED",
            FontFamily = new FontFamily("Consolas"),
            FontWeight = FontWeights.Bold,
            FontSize = 9,
            Foreground = ach.IsUnlocked ? (Brush)FindResource("NeonEmeraldBrush") : (Brush)FindResource("TextMutedBrush"),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        statusBadge.Child = statusText;

        rightStack.Children.Add(rewardBadge);
        rightStack.Children.Add(statusBadge);

        Grid.SetColumn(rightStack, 2);
        grid.Children.Add(rightStack);

        cardBorder.Child = grid;
        return cardBorder;
    }

    private void BtnFilterAchievements_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string filter)
        {
            _currentAchievementFilter = filter;
            _audio.PlayMenuClick();
            PopulateAchievements();
        }
    }

    private void ShowAchievementToast(Achievement ach)
    {
        Dispatcher.Invoke(() =>
        {
            TxtToastIcon.Text = ach.GlyphIcon;
            TxtToastTitle.Text = ach.Title.ToUpper();
            TxtToastDesc.Text = ach.Description;
            TxtToastXp.Text = $"+{ach.XpReward:N0} XP";

            AchievementToastBorder.Visibility = Visibility.Visible;
            _toastTimer.Stop();
            _toastTimer.Start();

            _audio.PlayAchievementUnlocked();
            _particles.EmitSectorLockBurst(0, 0, 400); // Celebratory fireworks
        });
    }

    #endregion

    #region Operative Profile Section Logic

    private void PopulateProfile()
    {
        TxtProfileRank.Text = $"RANK: {_profile.RankTitle}";
        TxtProfileLevel.Text = $"LEVEL {_profile.Level:D2}";
        TxtProfileXp.Text = $"{_profile.CurrentXP:N0} / {_profile.XPForNextLevel:N0} XP";
        PbProfileXp.Maximum = _profile.XPForNextLevel;
        PbProfileXp.Value = _profile.CurrentXP;

        TxtProfileEnergy.Text = $"{_profile.CurrentEnergy} / {_profile.MaxEnergy}";
        PbProfileEnergy.Maximum = _profile.MaxEnergy;
        PbProfileEnergy.Value = _profile.CurrentEnergy;

        TxtProfileDrones.Text = $"{_profile.ReconDronesAvailable} Charges Available";
        TxtProfileShields.Text = $"{_profile.BlastShieldCharges} Layer{(_profile.BlastShieldCharges == 1 ? "" : "s")} Ready";

        // Lifetime Telemetry Metrics
        TxtStatCellsCleared.Text = $"{_profile.Stats.TotalCellsCleared:N0}";
        TxtStatMinesFlagged.Text = $"{_profile.Stats.TotalMinesFlagged:N0}";
        TxtStatSectorsLocked.Text = $"{_profile.Stats.TotalSectorsLocked:N0}";
        TxtStatHighestCombo.Text = $"x{_profile.Stats.HighestComboMultiplier:F1}";
        TxtStatShieldsAbsorbed.Text = $"{_profile.Stats.TotalShieldsAbsorbed:N0}";
        TxtStatLongestStreak.Text = $"{_profile.Stats.LongestSafeStreak:N0}";
        TxtStatTotalXp.Text = $"{_profile.Stats.TotalXpEarned:N0}";
        TxtStatSectorsVisited.Text = $"{_profile.Stats.SectorsVisited:N0}";
    }

    private void BtnResetProfile_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "Are you sure you want to reset your Operative Service Record?\n\nThis will reset Level, XP, Energy, and Lifetime stats back to Level 1 defaults.",
            "RESET SERVICE RECORD // CONFIRMATION",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result == MessageBoxResult.Yes)
        {
            _profile.ResetProfile();
            PopulateProfile();
            UpdateHudStats();
            UpdateMainMenuSummary();
            _audio.PlayDetonation();
        }
    }

    #endregion

    #region Theme Matrix Section Logic

    private void BtnThemeSelect_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string themeId)
        {
            ThemeManager.SetTheme(themeId, Application.Current.Resources);
            _audio.PlayThemeSwitched();
        }
    }

    private void OnThemeApplied(ThemeDefinition theme)
    {
        _gridRenderer.ApplyTheme(theme);
        _profile.ActiveThemeId = theme.Id;
        _profile.SaveToDatabase();

        TxtStatusTheme.Text = theme.Name;
        TxtMenuTheme.Text = theme.Name;
        UpdateThemeBadges();
    }

    private void UpdateThemeBadges()
    {
        string currentId = ThemeManager.CurrentTheme.Id.ToLower();
        BadgeThemeCyberpunk.Visibility = (currentId == "cyberpunk") ? Visibility.Visible : Visibility.Collapsed;
        BadgeThemeMatrix.Visibility = (currentId == "matrix") ? Visibility.Visible : Visibility.Collapsed;
        BadgeThemeSynthwave.Visibility = (currentId == "synthwave") ? Visibility.Visible : Visibility.Collapsed;
        BadgeThemeSolaris.Visibility = (currentId == "solaris") ? Visibility.Visible : Visibility.Collapsed;
        BadgeThemeCrimson.Visibility = (currentId == "crimson") ? Visibility.Visible : Visibility.Collapsed;
    }

    #endregion

    #region HUD Stats & Simulation Loop

    private void UpdateHudStats()
    {
        Dispatcher.Invoke(() =>
        {
            TxtLevel.Text = $"{_profile.Level:D2}";
            TxtHudRankTitle.Text = $" [{_profile.RankTitle}]";
            TxtXpProgress.Text = $"{_profile.CurrentXP:N0} / {_profile.XPForNextLevel:N0}";
            PbXp.Maximum = _profile.XPForNextLevel;
            PbXp.Value = _profile.CurrentXP;

            TxtEnergy.Text = $"{_profile.CurrentEnergy} / {_profile.MaxEnergy}";
            PbEnergy.Maximum = _profile.MaxEnergy;
            PbEnergy.Value = _profile.CurrentEnergy;

            TxtCombo.Text = $"x{_profile.ComboMultiplier:F1}";

            TxtDroneCharges.Text = $"{_profile.ReconDronesAvailable}x";
            TxtShieldCharges.Text = $"{_profile.BlastShieldCharges} CHARGE{(_profile.BlastShieldCharges == 1 ? "" : "S")}";

            BannerTargeting.Visibility = _gameSession.IsTargetingDrone ? Visibility.Visible : Visibility.Collapsed;
        });
    }

    private void GameSession_OnSectorLocked(int cx, int cy)
    {
        Dispatcher.Invoke(() =>
        {
            TxtSectorStatus.Text = $"SECTOR [{cx}, {cy}] SECURED! +500 XP";
        });
    }

    private void GameSession_OnMineDetonated(int wx, int wy)
    {
        Dispatcher.Invoke(() =>
        {
            TxtSectorStatus.Text = $"⚠ MINE DETONATION AT [{wx}, {wy}]!";
        });
    }

    private void GameSession_OnDetonationTrauma(bool shieldAbsorbed)
    {
        Dispatcher.Invoke(() =>
        {
            if (shieldAbsorbed)
            {
                _camera.AddTrauma(0.35f);
            }
            else
            {
                _camera.AddTrauma(0.85f);
                HazardFlashBorder.Opacity = 0.85f;
            }
        });
    }

    private void Profile_OnComboShattered()
    {
        Dispatcher.Invoke(() =>
        {
            BannerComboShatter.Visibility = Visibility.Visible;
            _shatterTimer.Stop();
            _shatterTimer.Start();
        });
    }

    private void Profile_OnEnergyDepleted()
    {
        Dispatcher.Invoke(() =>
        {
            TxtGameOverCells.Text = $"{_profile.RunCellsCleared:N0}";
            TxtGameOverSectors.Text = $"{_profile.RunSectorsLocked:N0}";
            TxtGameOverCombo.Text = $"x{_profile.RunHighestCombo:F1}";
            TxtGameOverXp.Text = $"+{_profile.RunXpEarned:N0} XP";
            TxtGameOverDetonations.Text = $"{_profile.RunDetonations}";

            SwitchView(ViewMode.GameOverModal);
        });
    }

    private void OnCompositionRendering(object? sender, EventArgs e)
    {
        float dt = (float)_deltaStopwatch.Elapsed.TotalSeconds;
        _deltaStopwatch.Restart();

        // Trauma screen shake update
        _camera.UpdateShake(dt);

        // Hazard detonation red flash decay
        if (HazardFlashBorder.Opacity > 0)
        {
            HazardFlashBorder.Opacity = Math.Max(0.0f, (float)(HazardFlashBorder.Opacity - dt * 2.5f));
        }

        // Ambient camera pan drift during Main Menu
        if (_currentView == ViewMode.MainMenu)
        {
            _ambientTime += dt;
            _camera.X = 140.0f * MathF.Sin(_ambientTime * 0.12f);
            _camera.Y = 100.0f * MathF.Cos(_ambientTime * 0.09f);
        }

        // Update particle physics simulation
        _particles.Update(Math.Min(0.05f, dt));

        // Update chunk streaming with current viewport dimensions
        var (dpiX, dpiY) = GetDpiScaling();
        float viewportWidth = (float)(SkiaCanvas.ActualWidth * dpiX);
        float viewportHeight = (float)(SkiaCanvas.ActualHeight * dpiY);

        if (viewportWidth > 0 && viewportHeight > 0)
        {
            _chunkManager.UpdateViewport(_camera, viewportWidth, viewportHeight);
            _profile.RecordSectorVisited(_chunkManager.ActiveChunks.Count);
        }

        // Redraw Skia canvas
        SkiaCanvas.InvalidateVisual();
    }

    #endregion

    #region Window Controls & Hotkeys

    private void MainWindow_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (_currentView == ViewMode.InGame)
            {
                SwitchView(ViewMode.Pause);
            }
            else if (_currentView == ViewMode.Pause)
            {
                SwitchView(ViewMode.InGame);
            }
            else if (_currentView == ViewMode.NewGameModal)
            {
                SwitchView(_modalReturnView);
            }
            else if (_currentView == ViewMode.GameOverModal)
            {
                SwitchView(ViewMode.MainMenu);
            }
            else if (_currentView != ViewMode.MainMenu)
            {
                SwitchView(ViewMode.MainMenu);
            }
            e.Handled = true;
            return;
        }

        // In-game specific hotkeys
        if (_currentView == ViewMode.InGame)
        {
            if (e.Key == Key.D1 || e.Key == Key.NumPad1)
            {
                ToggleDroneTargeting();
            }
            else if (e.Key == Key.M)
            {
                ToggleAudio();
            }
            else if (e.Key == Key.Home || e.Key == Key.R || e.Key == Key.D0)
            {
                _camera.Reset();
                UpdateTelemetry(0, 0);
            }
        }
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            if (e.ClickCount == 2)
            {
                ToggleMaximize();
            }
            else
            {
                DragMove();
            }
        }
    }

    private void BtnMinimize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void BtnMaximize_Click(object sender, RoutedEventArgs e)
    {
        ToggleMaximize();
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void ToggleMaximize()
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void BtnResetCamera_Click(object sender, RoutedEventArgs e)
    {
        _camera.Reset();
        UpdateTelemetry(0, 0);
        SkiaCanvas.InvalidateVisual();
    }

    private void BtnDrone_Click(object sender, RoutedEventArgs e)
    {
        ToggleDroneTargeting();
    }

    private void ToggleDroneTargeting()
    {
        if (_profile.ReconDronesAvailable <= 0)
        {
            TxtSectorStatus.Text = "NO RECON DRONES AVAILABLE! SECURE SECTORS TO ACQUIRE MORE.";
            return;
        }

        _gameSession.IsTargetingDrone = !_gameSession.IsTargetingDrone;
        BannerTargeting.Visibility = _gameSession.IsTargetingDrone ? Visibility.Visible : Visibility.Collapsed;
        Cursor = _gameSession.IsTargetingDrone ? Cursors.Cross : Cursors.Arrow;
    }

    private void BtnAudioToggle_Click(object sender, RoutedEventArgs e)
    {
        ToggleAudio();
    }

    private void ToggleAudio()
    {
        _audio.IsMuted = !_audio.IsMuted;
        TxtAudioToggle.Text = _audio.IsMuted ? "🔇 SFX: MUTED [M]" : "🔊 SFX: ON [M]";
    }

    #endregion

    #region SkiaSharp Canvas & Input Handling

    private void SkiaCanvas_PaintSurface(object sender, SKPaintSurfaceEventArgs e)
    {
        // 1. Calculate FPS
        _frameCount++;
        double elapsedSeconds = _fpsStopwatch.Elapsed.TotalSeconds;
        if (elapsedSeconds - _lastFpsUpdate >= 0.35)
        {
            _currentFps = _frameCount / (elapsedSeconds - _lastFpsUpdate);
            _frameCount = 0;
            _lastFpsUpdate = elapsedSeconds;
            TxtFps.Text = $"{_currentFps:F1} FPS";
        }

        // 2. Render Infinite Grid, Chunks, and Particles
        int pixelWidth = e.Info.Width;
        int pixelHeight = e.Info.Height;
        SKCanvas canvas = e.Surface.Canvas;

        _gridRenderer.Render(canvas, pixelWidth, pixelHeight, _camera, _lastMouseScreenPixel, _chunkManager, _particles);
    }

    private void SkiaCanvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_currentView != ViewMode.InGame) return;

        Point pos = e.GetPosition(SkiaCanvas);
        _mouseDownPos = pos;
        _hasDragged = false;

        if (e.ChangedButton == MouseButton.Middle)
        {
            _isPanning = true;
            _lastMousePosition = pos;
            SkiaCanvas.CaptureMouse();
            Cursor = Cursors.SizeAll;
            e.Handled = true;
        }
        else if (e.ChangedButton == MouseButton.Right)
        {
            _isPanning = true;
            _lastMousePosition = pos;
            SkiaCanvas.CaptureMouse();
            e.Handled = true;
        }
    }

    private void SkiaCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        Point currentPos = e.GetPosition(SkiaCanvas);
        var (dpiX, dpiY) = GetDpiScaling();

        float screenPixelX = (float)(currentPos.X * dpiX);
        float screenPixelY = (float)(currentPos.Y * dpiY);
        _lastMouseScreenPixel = new SKPoint(screenPixelX, screenPixelY);

        if (_currentView != ViewMode.InGame) return;

        if (_isPanning)
        {
            double deltaDipX = currentPos.X - _lastMousePosition.X;
            double deltaDipY = currentPos.Y - _lastMousePosition.Y;

            if (Math.Abs(currentPos.X - _mouseDownPos.X) > 4 || Math.Abs(currentPos.Y - _mouseDownPos.Y) > 4)
            {
                _hasDragged = true;
                Cursor = Cursors.SizeAll;
            }

            _camera.Pan((float)(deltaDipX * dpiX), (float)(deltaDipY * dpiY));
            _lastMousePosition = currentPos;
        }

        float viewportWidth = (float)(SkiaCanvas.ActualWidth * dpiX);
        float viewportHeight = (float)(SkiaCanvas.ActualHeight * dpiY);
        if (viewportWidth > 0 && viewportHeight > 0)
        {
            SKPoint worldPoint = _camera.ScreenToWorld(_lastMouseScreenPixel.Value, viewportWidth, viewportHeight);
            UpdateTelemetry(worldPoint.X, worldPoint.Y);
        }
    }

    private void SkiaCanvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_currentView != ViewMode.InGame) return;

        Point currentPos = e.GetPosition(SkiaCanvas);
        var (dpiX, dpiY) = GetDpiScaling();

        float viewportWidth = (float)(SkiaCanvas.ActualWidth * dpiX);
        float viewportHeight = (float)(SkiaCanvas.ActualHeight * dpiY);

        SKPoint screenPixel = new SKPoint((float)(currentPos.X * dpiX), (float)(currentPos.Y * dpiY));
        SKPoint worldPoint = _camera.ScreenToWorld(screenPixel, viewportWidth, viewportHeight);
        var (cellX, cellY) = Camera.WorldToCell(worldPoint.X, worldPoint.Y);

        if (_isPanning)
        {
            _isPanning = false;
            SkiaCanvas.ReleaseMouseCapture();
            Cursor = _gameSession.IsTargetingDrone ? Cursors.Cross : Cursors.Arrow;

            if (e.ChangedButton == MouseButton.Right && !_hasDragged)
            {
                _gameSession.ToggleFlag(cellX, cellY);
                SkiaCanvas.InvalidateVisual();
                e.Handled = true;
                return;
            }

            if (e.ChangedButton == MouseButton.Middle && !_hasDragged)
            {
                _gameSession.ChordCell(cellX, cellY);
                SkiaCanvas.InvalidateVisual();
                e.Handled = true;
                return;
            }
        }

        if (e.ChangedButton == MouseButton.Left)
        {
            if (!_hasDragged)
            {
                if (e.ClickCount == 2)
                {
                    _gameSession.ChordCell(cellX, cellY);
                }
                else
                {
                    _gameSession.RevealCell(cellX, cellY);
                }

                if (!_gameSession.IsTargetingDrone)
                {
                    Cursor = Cursors.Arrow;
                }
            }
            SkiaCanvas.InvalidateVisual();
            e.Handled = true;
        }
    }

    private void SkiaCanvas_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_currentView != ViewMode.InGame) return;

        Point mousePos = e.GetPosition(SkiaCanvas);
        var (dpiX, dpiY) = GetDpiScaling();

        float screenPixelX = (float)(mousePos.X * dpiX);
        float screenPixelY = (float)(mousePos.Y * dpiY);

        float viewportWidth = (float)(SkiaCanvas.ActualWidth * dpiX);
        float viewportHeight = (float)(SkiaCanvas.ActualHeight * dpiY);

        float zoomFactor = e.Delta > 0 ? 1.15f : 1.0f / 1.15f;
        _camera.ZoomAt(new SKPoint(screenPixelX, screenPixelY), zoomFactor, viewportWidth, viewportHeight);

        SKPoint worldPoint = _camera.ScreenToWorld(new SKPoint(screenPixelX, screenPixelY), viewportWidth, viewportHeight);
        UpdateTelemetry(worldPoint.X, worldPoint.Y);

        SkiaCanvas.InvalidateVisual();
        e.Handled = true;
    }

    private void SkiaCanvas_MouseLeave(object sender, MouseEventArgs e)
    {
        _lastMouseScreenPixel = null;
        if (_isPanning)
        {
            _isPanning = false;
            SkiaCanvas.ReleaseMouseCapture();
            Cursor = Cursors.Arrow;
        }
    }

    private void UpdateTelemetry(float worldX, float worldY)
    {
        var (cellX, cellY) = Camera.WorldToCell(worldX, worldY);
        var (chunkX, chunkY, _, _) = Camera.CellToChunkAndLocal(cellX, cellY);

        TxtWorldPos.Text = $"X: {worldX:F1}  Y: {worldY:F1}";
        TxtCellPos.Text = $"[{cellX}, {cellY}]";
        TxtChunkPos.Text = $"[{chunkX}, {chunkY}]";
        TxtZoom.Text = $"{_camera.Zoom * 100:F0}%";
        TxtChunkCount.Text = $"{_chunkManager.ActiveChunks.Count} SECTORS ACTIVE (SQLite)";
    }

    private (double DpiX, double DpiY) GetDpiScaling()
    {
        PresentationSource source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget != null)
        {
            return (source.CompositionTarget.TransformToDevice.M11,
                    source.CompositionTarget.TransformToDevice.M22);
        }
        return (1.0, 1.0);
    }

    #endregion
}

using System;
using System.Windows;
using System.Windows.Input;
using MoonLiveCaptions.Helpers;
using MoonLiveCaptions.ViewModels;

namespace MoonLiveCaptions
{
    public partial class CaptionWindow : Window
    {
        private CaptionViewModel _vm;
        private AppBarManager    _appBar;

        private bool   _autoScrollEnabled = true;
        private bool   _scrollChangedByCode;

        private const double DockedHeight = 180;

        // Saved floating position/size for round-trip with docked modes
        private double _savedLeft   = double.NaN;
        private double _savedTop    = double.NaN;
        private double _savedWidth  = 720;
        private double _savedHeight = 190;

        public CaptionWindow()
        {
            InitializeComponent();

            _appBar = new AppBarManager(this);
            _vm     = new CaptionViewModel();

            _vm.DisplayModeChanged  += OnDisplayModeChanged;
            _vm.CaptionTextUpdated  += OnCaptionTextUpdated;
            _vm.CloseRequested      += (_, __) => Close();

            DataContext = _vm;

            // Restore saved window position/size
            _savedLeft   = AppSettings.WindowLeft;
            _savedTop    = AppSettings.WindowTop;
            _savedWidth  = double.IsNaN(AppSettings.WindowWidth)  ? 720 : AppSettings.WindowWidth;
            _savedHeight = double.IsNaN(AppSettings.WindowHeight) ? 190 : AppSettings.WindowHeight;

            Loaded   += OnLoaded;
            Closing  += OnClosing;
            SizeChanged += OnSizeChanged;
        }

        // ═══════════════════════════════════════════════════════
        // Lifecycle
        // ═══════════════════════════════════════════════════════

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            // Apply initial floating position
            ApplyFloatingMode();
        }

        private void OnClosing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            // Persist window position/size
            AppSettings.WindowLeft   = _savedLeft;
            AppSettings.WindowTop    = _savedTop;
            AppSettings.WindowWidth  = _savedWidth;
            AppSettings.WindowHeight = _savedHeight;
            AppSettings.Save();

            _appBar?.Dispose();
            _vm?.Dispose();
        }

        private void OnSizeChanged(object sender, SizeChangedEventArgs e)
        {
            // Keep saved size in sync while floating
            if (_vm?.IsFloating == true)
            {
                _savedWidth  = ActualWidth;
                _savedHeight = ActualHeight;
            }
        }

        // ═══════════════════════════════════════════════════════
        // Display mode
        // ═══════════════════════════════════════════════════════

        private void OnDisplayModeChanged(object sender, DisplayMode mode)
        {
            switch (mode)
            {
                case DisplayMode.Top:
                    SaveFloatingState();
                    ApplyDockedMode(AppBarManager.ABE_TOP);
                    break;
                case DisplayMode.Bottom:
                    SaveFloatingState();
                    ApplyDockedMode(AppBarManager.ABE_BOTTOM);
                    break;
                default:
                    ApplyFloatingMode();
                    break;
            }
        }

        private void ApplyDockedMode(uint edge)
        {
            ResizeMode = ResizeMode.NoResize;
            MainContainer.CornerRadius = new CornerRadius(0);
            MainContainer.BorderThickness = new Thickness(0);
            // Dock() calls PositionAppBar() internally, which sets Width/Height
            _appBar.Dock(edge, DockedHeight);
        }

        private void ApplyFloatingMode()
        {
            _appBar.Undock();
            MainContainer.CornerRadius = new CornerRadius(8);
            MainContainer.BorderThickness = new Thickness(1);
            ResizeMode = ResizeMode.CanResizeWithGrip;

            if (!double.IsNaN(_savedLeft) && !double.IsNaN(_savedTop))
            {
                Left = _savedLeft;
                Top  = _savedTop;
            }
            else
            {
                // Default position: bottom-centre of primary screen
                var screen = SystemParameters.WorkArea;
                Left = (screen.Width  - _savedWidth)  / 2;
                Top  = screen.Height  - _savedHeight - 40;
            }

            Width  = _savedWidth;
            Height = _savedHeight;
        }

        private void SaveFloatingState()
        {
            if (_vm?.IsFloating == true)
            {
                _savedLeft   = Left;
                _savedTop    = Top;
                _savedWidth  = ActualWidth;
                _savedHeight = ActualHeight;
            }
        }

        // ═══════════════════════════════════════════════════════
        // Header drag
        // ═══════════════════════════════════════════════════════

        private void HeaderBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_vm?.IsFloating == true && e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
                // Update saved position after drag
                _savedLeft = Left;
                _savedTop  = Top;
            }
        }

        // ═══════════════════════════════════════════════════════
        // Caption auto-scroll
        // ═══════════════════════════════════════════════════════

        private void CaptionScroller_ScrollChanged(object sender, System.Windows.Controls.ScrollChangedEventArgs e)
        {
            if (_scrollChangedByCode)
                return;

            // If user scrolled up → pause auto-scroll; if at bottom → resume
            if (e.VerticalChange < 0)
            {
                _autoScrollEnabled = false;
            }
            else
            {
                double remaining = CaptionScroller.ScrollableHeight - CaptionScroller.VerticalOffset;
                if (remaining < 2)
                    _autoScrollEnabled = true;
            }
        }

        private void OnCaptionTextUpdated(object sender, EventArgs e)
        {
            if (!_autoScrollEnabled) return;

            _scrollChangedByCode = true;
            try
            {
                CaptionScroller.ScrollToEnd();
            }
            finally
            {
                _scrollChangedByCode = false;
            }
        }
    }
}

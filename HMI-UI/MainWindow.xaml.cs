using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using XGCommLibDemo;

namespace ElevatorPLC
{

    public partial class MainWindow : Window
    {
        // ── 로직 & 타이머 ────────────────────────────────────
        private readonly ElevatorLogic  _logic;
        private readonly DispatcherTimer _moveTimer;
        private readonly DispatcherTimer _doorTimer;
        private readonly DispatcherTimer _processTimer;

        // ── PLC 통신 ─────────────────────────────────────────
        private readonly XGCommSocket    _plc = new XGCommSocket();
        private readonly DispatcherTimer _keepAliveTimer;
        private bool _plcConnected = false;

        // ── 샤프트 UI 참조 ───────────────────────────────────
        private readonly Dictionary<int, Border>    _shaftCells   = new Dictionary<int, Border>();
        private readonly Dictionary<int, TextBlock> _floorNumDisp = new Dictionary<int, TextBlock>();
        private readonly Dictionary<int, TextBlock> _upArrows     = new Dictionary<int, TextBlock>();
        private readonly Dictionary<int, TextBlock> _downArrows   = new Dictionary<int, TextBlock>();
        private readonly Dictionary<int, Button>    _floorBtns    = new Dictionary<int, Button>();
        // 층 버튼 배치 순서 (6,5 / 3,4 / 1,2)
        private static readonly int[] BtnOrder = { 5, 4, 3, 2, 1 };
        private const int FLOOR_COUNT = 5;

        // ── 생성자 ───────────────────────────────────────────
        public MainWindow()
        {
            InitializeComponent();

            _logic = new ElevatorLogic();

            _moveTimer    = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(2000) };
            _doorTimer    = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(5000) };
            _processTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600)  };
            _keepAliveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _keepAliveTimer.Tick += KeepAliveTimer_Tick;

            Loaded += OnLoaded;
        }

        // ── 로드 완료 ────────────────────────────────────────
        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            BuildShaftUI();
            BuildFloorGuide();
            BuildFloorButtons();

            // PLC 연결 시작 (비동기, UI 블로킹 없음)
            _ = ConnectPlcAsync();

            // 이벤트 연결
            _logic.StateChanged += OnStateChanged;
            _logic.DoorOpened   += OnDoorOpened;
            _logic.DoorClosed   += OnDoorClosed;

            // 타이머 연결
            _moveTimer.Tick    += MoveTimer_Tick;
            _doorTimer.Tick    += DoorTimer_Tick;
            _processTimer.Tick += ProcessTimer_Tick;

            // 초기 UI 설정
            Dispatcher.BeginInvoke(new Action(() =>
            {
                UpdateCarPosition(false);
                UpdateAllUI(1, 0, ElevatorState.Idle, new List<int>());
                _logic.TriggerDoorOpen();
                _doorTimer.Start();
            }), DispatcherPriority.Loaded);
        }

        // 이벤트 핸들러
        private void OnDoorOpened(object sender, EventArgs e) { AnimateDoorOpen();  }
        private void OnDoorClosed(object sender, EventArgs e) { AnimateDoorClose(); }

        // ══════════════════════════════════════════════════════
        // 샤프트 UI 생성
        // ══════════════════════════════════════════════════════
        private void BuildShaftUI()
        {
            ShaftItemsControl.Items.Clear();
            _shaftCells.Clear();
            _floorNumDisp.Clear();
            _upArrows.Clear();
            _downArrows.Clear();

            foreach (FloorInfo floor in ElevatorLogic.Floors)
            {
                Grid row = CreateShaftRow(floor);
                ShaftItemsControl.Items.Add(row);
            }
        }

        private Grid CreateShaftRow(FloorInfo floor)
        {
            var root = new Grid { Margin = new Thickness(0, 1, 0, 1) };
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });

            // ── 층 레이블 ──
            var labelPanel = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment   = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 4, 0)
            };
            labelPanel.Children.Add(new TextBlock
            {
                Text       = floor.Number + "층",
                FontFamily = new FontFamily("Courier New"),
                FontSize   = 10,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0xA0, 0xBC, 0xE0)),
            });

            string sensorAddr;
            if (ElevatorLogic.FloorSensorAddr.TryGetValue(floor.Number, out sensorAddr))
            {
                labelPanel.Children.Add(new TextBlock
                {
                    Text       = sensorAddr,
                    FontFamily = new FontFamily("Courier New"),
                    FontSize   = 8,
                    Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xAA, 0x55)),
                });
            }
            Grid.SetColumn(labelPanel, 0);
            root.Children.Add(labelPanel);

            // ── 샤프트 셀 ──
            var cell = new Border
            {
                Background      = new SolidColorBrush(Color.FromRgb(0x1E, 0x2A, 0x40)),
                BorderBrush     = new SolidColorBrush(Color.FromRgb(0x4A, 0x58, 0x78)),
                BorderThickness = new Thickness(1),
                CornerRadius    = new CornerRadius(2),
                Margin          = new Thickness(0, 1, 0, 1),
            };
            _shaftCells[floor.Number] = cell;

            var cellGrid = new Grid();

            // 레일 중앙선
            cellGrid.Children.Add(new Rectangle
            {
                Width               = 1,
                Fill                = new SolidColorBrush(Color.FromRgb(0x5A, 0x68, 0x88)),
                HorizontalAlignment = HorizontalAlignment.Center,
            });

            // 상단 인디케이터 행 + LED 래퍼
            var topWrap = new Grid();
            topWrap.ColumnDefinitions.Add(new ColumnDefinition());
            topWrap.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var topRow = new StackPanel
            {
                Orientation         = Orientation.Horizontal,
                VerticalAlignment   = VerticalAlignment.Top,
                Margin              = new Thickness(3, 2, 0, 0),
            };

            if (floor.Number < 6)
            {
                var ua = MakeIndArrowText("▲");
                _upArrows[floor.Number] = ua;
                topRow.Children.Add(WrapArrow(ua));
            }
            if (floor.Number > 1)
            {
                var da = MakeIndArrowText("▼");
                _downArrows[floor.Number] = da;
                topRow.Children.Add(WrapArrow(da));
            }

            Grid.SetColumn(topRow, 0);
            topWrap.Children.Add(topRow);

            // 층 번호 LED
            var numTb = new TextBlock
            {
                Text                = "1",
                FontFamily          = new FontFamily("Courier New"),
                FontSize            = 10,
                FontWeight          = FontWeights.Bold,
                Foreground          = new SolidColorBrush(Color.FromRgb(0x55, 0xFF, 0x88)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment   = VerticalAlignment.Center,
            };
            var numBorder = new Border
            {
                Background      = new SolidColorBrush(Color.FromRgb(0x0C, 0x20, 0x0E)),
                BorderBrush     = new SolidColorBrush(Color.FromRgb(0x22, 0x66, 0x33)),
                BorderThickness = new Thickness(1),
                CornerRadius    = new CornerRadius(1),
                Width           = 20,
                Height          = 14,
                Margin          = new Thickness(0, 2, 2, 0),
                VerticalAlignment = VerticalAlignment.Top,
                Child           = numTb,
            };
            _floorNumDisp[floor.Number] = numTb;
            Grid.SetColumn(numBorder, 1);
            topWrap.Children.Add(numBorder);

            cellGrid.Children.Add(topWrap);
            cell.Child = cellGrid;
            Grid.SetColumn(cell, 1);
            root.Children.Add(cell);

            // ── 호출 버튼 ──
            var callPanel = new StackPanel
            {
                VerticalAlignment   = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(3, 0, 0, 0),
            };

            CallAddrPair ca;
            if (ElevatorLogic.CallAddr.TryGetValue(floor.Number, out ca))
            {
                if (ca.Up != null)
                {
                    callPanel.Children.Add(MakeCallButton("▲", floor.Number, "Up"));
                    callPanel.Children.Add(new TextBlock
                    {
                        Text                = ca.Up,
                        FontFamily          = new FontFamily("Courier New"),
                        FontSize            = 7,
                        Foreground          = new SolidColorBrush(Color.FromRgb(0xFF, 0xAA, 0x55)),
                        HorizontalAlignment = HorizontalAlignment.Center,
                    });
                }
                if (ca.Down != null)
                {
                    callPanel.Children.Add(MakeCallButton("▼", floor.Number, "Down"));
                    callPanel.Children.Add(new TextBlock
                    {
                        Text                = ca.Down,
                        FontFamily          = new FontFamily("Courier New"),
                        FontSize            = 7,
                        Foreground          = new SolidColorBrush(Color.FromRgb(0xFF, 0xAA, 0x55)),
                        HorizontalAlignment = HorizontalAlignment.Center,
                    });
                }
            }

            Grid.SetColumn(callPanel, 2);
            root.Children.Add(callPanel);

            return root;
        }

        private TextBlock MakeIndArrowText(string symbol)
        {
            return new TextBlock
            {
                Text                = symbol,
                FontSize            = 8,
                Foreground          = new SolidColorBrush(Color.FromRgb(0x9A, 0xB0, 0xCC)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment   = VerticalAlignment.Center,
            };
        }

        private Border WrapArrow(TextBlock tb)
        {
            return new Border
            {
                Background      = new SolidColorBrush(Color.FromRgb(0x3A, 0x4A, 0x68)),
                BorderBrush     = new SolidColorBrush(Color.FromRgb(0x6A, 0x7A, 0x98)),
                BorderThickness = new Thickness(1),
                CornerRadius    = new CornerRadius(2),
                Width           = 16,
                Height          = 13,
                Margin          = new Thickness(0, 0, 2, 0),
                Child           = tb,
            };
        }

        private Button MakeCallButton(string symbol, int floor, string direction)
        {
            var btn = new Button
            {
                Content         = symbol,
                Width           = 20,
                Height          = 17,
                FontSize        = 8,
                Background      = new SolidColorBrush(Color.FromRgb(0x3A, 0x4A, 0x68)),
                Foreground      = new SolidColorBrush(Color.FromRgb(0xA0, 0xBC, 0xD8)),
                BorderBrush     = new SolidColorBrush(Color.FromRgb(0x6A, 0x7A, 0x98)),
                BorderThickness = new Thickness(1),
                Cursor          = System.Windows.Input.Cursors.Hand,
                Margin          = new Thickness(0, 1, 0, 1),
                Tag             = floor,
            };
            int capturedFloor = floor;
            string capturedDir = direction;
            btn.PreviewMouseLeftButtonDown += (s, e) =>
            {
                long bit = GetCallButtonBit(capturedFloor, capturedDir);
                if (bit >= 0) WritePlcBit(bit, 1);
                RequestFloor(capturedFloor);
            };
            btn.PreviewMouseLeftButtonUp += (s, e) =>
            {
                long bit = GetCallButtonBit(capturedFloor, capturedDir);
                if (bit >= 0) WritePlcBit(bit, 0);
            };
            return btn;
        }

        // ══════════════════════════════════════════════════════
        // 층 안내판 생성
        // ══════════════════════════════════════════════════════
        private void BuildFloorGuide()
        {
            FloorGuideControl.Items.Clear();
            foreach (FloorInfo f in ElevatorLogic.Floors)
            {
                var rowGrid = new Grid { Margin = new Thickness(0) };
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition());

                var numTb = new TextBlock
                {
                    Text              = f.Number + "층",
                    FontFamily        = new FontFamily("Courier New"),
                    FontSize          = 13,
                    FontWeight        = FontWeights.Bold,
                    Foreground        = new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99)),
                    Margin            = new Thickness(12, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Tag               = "guide_num_" + f.Number,
                };
                var nameTb = new TextBlock
                {
                    Text              = f.Name,
                    FontFamily        = new FontFamily("Malgun Gothic"),
                    FontSize          = 11,
                    Foreground        = new SolidColorBrush(Color.FromRgb(0x77, 0x77, 0x77)),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin            = new Thickness(6, 0, 0, 0),
                    Tag               = "guide_name_" + f.Number,
                };
                Grid.SetColumn(nameTb, 1);
                rowGrid.Children.Add(numTb);
                rowGrid.Children.Add(nameTb);

                var wrapper = new Border
                {
                    Child           = rowGrid,
                    BorderBrush     = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E)),
                    BorderThickness = new Thickness(0, 0, 0, 1),
                    Padding         = new Thickness(0, 6, 0, 6),
                    Tag             = "guide_" + f.Number,
                };
                FloorGuideControl.Items.Add(wrapper);
            }
        }

        // ══════════════════════════════════════════════════════
        // 컨트롤 패널 버튼 생성
        // ══════════════════════════════════════════════════════
        private void BuildFloorButtons()
        {
            FloorBtnGrid.Children.Clear();
            _floorBtns.Clear();

            foreach (int num in BtnOrder)
            {
                var btn = new Button
                {
                    Style   = (Style)Resources["FloorBtnStyle"],
                    Content = num.ToString(),
                    Tag     = num,
                };
                int capturedNum = num;
                btn.PreviewMouseLeftButtonDown += (s, e) =>
                {
                    WritePlcBit(GetFloorButtonBit(capturedNum), 1);
                    RequestFloor(capturedNum);
                };
                btn.PreviewMouseLeftButtonUp += (s, e) =>
                    WritePlcBit(GetFloorButtonBit(capturedNum), 0);
                _floorBtns[num] = btn;
                FloorBtnGrid.Children.Add(btn);
            }
        }

        // ══════════════════════════════════════════════════════
        // 엘리베이터 제어
        // ══════════════════════════════════════════════════════
        private void RequestFloor(int floor)
        {
            _logic.RequestFloor(floor);
            if (_logic.State == ElevatorState.Moving) return;
            if (_logic.HasPending)
            {
                _doorTimer.Stop();
                _logic.StartMoving();
                _moveTimer.Start();
            }
        }

        private void MoveTimer_Tick(object sender, EventArgs e)
        {
            bool arrived = _logic.StepMove();
            UpdateCarPosition(true);
            if (arrived)
            {
                _moveTimer.Stop();
                // 도착 후 1초 뒤 문 열림
                var arrivalDelay = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000) };
                arrivalDelay.Tick += (s, ev) =>
                {
                    arrivalDelay.Stop();
                    _logic.TriggerDoorOpen();
                    _doorTimer.Start();
                };
                arrivalDelay.Start();
            }
        }

        private void DoorTimer_Tick(object sender, EventArgs e)
        {
            _doorTimer.Stop();
            _logic.TriggerDoorClose();
            _processTimer.Start();
        }

        private void ProcessTimer_Tick(object sender, EventArgs e)
        {
            _processTimer.Stop();
            if (_logic.HasPending && _logic.StartMoving())
                _moveTimer.Start();
        }

        // ══════════════════════════════════════════════════════
        // 상태 변경 → UI 반영
        // ══════════════════════════════════════════════════════
        private void OnStateChanged(object sender, ElevatorEventArgs e)
        {
            int floor      = e.CurrentFloor;
            int dir        = e.Direction;
            ElevatorState state = e.State;
            List<int> queue    = e.Queue;

            Dispatcher.BeginInvoke(new Action(() =>
                UpdateAllUI(floor, dir, state, queue)));
        }

        private void UpdateAllUI(int floor, int dir, ElevatorState state, List<int> queue)
        {
            // ── 컨트롤패널 디스플레이 ──
            FloorDisplay.Text = floor.ToString();
            DirDisplay.Text   = dir > 0 ? "▲" : dir < 0 ? "▼" : "";

            // ── 방향 인디케이터 (도어뷰) ──
            DirIndicator.Text       = dir > 0 ? "▲" : dir < 0 ? "▼" : "";
            DirIndicator.Visibility = (state == ElevatorState.Moving)
                                      ? Visibility.Visible : Visibility.Hidden;

            // ── 상태 텍스트 ──
            if (state == ElevatorState.Moving)
                StatusText.Text = "상태: " + (dir > 0 ? "상승중" : "하강중");
            else if (state == ElevatorState.DoorOpen)
                StatusText.Text = "상태: 문 열림";
            else
                StatusText.Text = "상태: 대기중";

            // ── 샤프트 각 층 업데이트 ──
            foreach (FloorInfo f in ElevatorLogic.Floors)
            {
                // LED 숫자
                TextBlock numTb;
                if (_floorNumDisp.TryGetValue(f.Number, out numTb))
                    numTb.Text = floor.ToString();

                // 셀 강조
                Border cell;
                if (_shaftCells.TryGetValue(f.Number, out cell))
                {
                    if (f.Number == floor)
                    {
                        cell.Background  = new SolidColorBrush(Color.FromRgb(0x2A, 0x1E, 0x0E));
                        cell.BorderBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xAA, 0x55));
                        cell.Effect      = new DropShadowEffect
                        {
                            Color       = Color.FromRgb(0xFF, 0xAA, 0x55),
                            BlurRadius  = 10,
                            Opacity     = 0.35,
                            ShadowDepth = 0
                        };
                    }
                    else
                    {
                        cell.Background  = new SolidColorBrush(Color.FromRgb(0x1E, 0x2A, 0x40));
                        cell.BorderBrush = new SolidColorBrush(Color.FromRgb(0x4A, 0x58, 0x78));
                        cell.Effect      = null;
                    }
                }

                // 화살표 색상
                bool isMovingHere = (state == ElevatorState.Moving && f.Number == floor);
                TextBlock ua;
                if (_upArrows.TryGetValue(f.Number, out ua))
                    ua.Foreground = (isMovingHere && dir > 0)
                        ? new SolidColorBrush(Color.FromRgb(0xFF, 0x77, 0x44))
                        : new SolidColorBrush(Color.FromRgb(0x9A, 0xB0, 0xCC));
                TextBlock da;
                if (_downArrows.TryGetValue(f.Number, out da))
                    da.Foreground = (isMovingHere && dir < 0)
                        ? new SolidColorBrush(Color.FromRgb(0x66, 0xAA, 0xFF))
                        : new SolidColorBrush(Color.FromRgb(0x9A, 0xB0, 0xCC));
            }

            // ── 층 안내판 ──
            foreach (object item in FloorGuideControl.Items)
            {
                Border wrapper = item as Border;
                if (wrapper == null) continue;

                string tag = wrapper.Tag as string;
                if (tag == null || !tag.StartsWith("guide_")) continue;

                int fNum;
                if (!int.TryParse(tag.Replace("guide_", ""), out fNum)) continue;

                bool isActive = (fNum == floor);
                wrapper.Background = isActive
                    ? new SolidColorBrush(Color.FromArgb(40, 0xFF, 0xAA, 0x55))
                    : Brushes.Transparent;

                Grid g = wrapper.Child as Grid;
                if (g == null) continue;
                foreach (object child in g.Children)
                {
                    TextBlock tb = child as TextBlock;
                    if (tb == null) continue;
                    string tbTag = tb.Tag as string;
                    if (tbTag == null) continue;

                    if (tbTag.StartsWith("guide_num"))
                        tb.Foreground = isActive
                            ? new SolidColorBrush(Color.FromRgb(0xFF, 0xAA, 0x55))
                            : new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99));
                    else if (tbTag.StartsWith("guide_name"))
                        tb.Foreground = isActive
                            ? new SolidColorBrush(Color.FromRgb(0xFF, 0xCC, 0x88))
                            : new SolidColorBrush(Color.FromRgb(0x77, 0x77, 0x77));
                }
            }

            // ── 컨트롤패널 버튼 lit ──
            foreach (KeyValuePair<int, Button> kv in _floorBtns)
            {
                bool lit = queue.Contains(kv.Key);
                kv.Value.Foreground = lit
                    ? new SolidColorBrush(Color.FromRgb(0x55, 0x33, 0x00))
                    : new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33));

                // Glow ellipse 조절
                kv.Value.ApplyTemplate();
                Ellipse glow = kv.Value.Template.FindName("Glow", kv.Value) as Ellipse;
                if (glow != null)
                    glow.Opacity = lit ? 1.0 : 0.0;
            }

            // ── 내부 배경 ──
            FloorInfo info = ElevatorLogic.Floors.Find(f => f.Number == floor);
            if (info != null && InteriorBg != null)
            {
                var stops = new GradientStopCollection();
                if (info.WarmBg)
                {
                    stops.Add(new GradientStop(Color.FromRgb(0xF0, 0xE8, 0xD8), 0));
                    stops.Add(new GradientStop(Color.FromRgb(0xD8, 0xC8, 0xA8), 0.6));
                    stops.Add(new GradientStop(Color.FromRgb(0xC0, 0xA8, 0x78), 1));
                }
                else
                {
                    stops.Add(new GradientStop(Color.FromRgb(0xE8, 0xF0, 0xF8), 0));
                    stops.Add(new GradientStop(Color.FromRgb(0xC0, 0xD0, 0xE0), 0.6));
                    stops.Add(new GradientStop(Color.FromRgb(0xA0, 0xB8, 0xCC), 1));
                }
                InteriorBg.Fill = new LinearGradientBrush(stops, 90);
                InteriorFloorName.Text = info.Name;
                InteriorFloorNum.Text  = info.Number + "층";
            }
        }

        // ══════════════════════════════════════════════════════
        // 카 위치 애니메이션
        // ══════════════════════════════════════════════════════
        private void UpdateCarPosition(bool animated)
        {
            double canvasH = ShaftCanvas.ActualHeight;
            if (canvasH <= 0) canvasH = 580;

            double cellH  = canvasH / FLOOR_COUNT;
            int    idx    = _logic.CurrentFloor - 1;        // 0 = 1층
            double bottom = idx * cellH + (cellH - ElevatorCar.Height) / 2.0;
            double top    = canvasH - bottom - ElevatorCar.Height;

            if (animated)
            {
                var anim = new DoubleAnimation(top, new Duration(TimeSpan.FromMilliseconds(1800)))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
                };
                ElevatorCar.BeginAnimation(Canvas.TopProperty, anim);
            }
            else
            {
                Canvas.SetTop(ElevatorCar, top);
            }

            ShaftCanvas.SizeChanged -= ShaftCanvas_SizeChanged;
            ShaftCanvas.SizeChanged += ShaftCanvas_SizeChanged;
            CenterCarHorizontally();
        }

        private void ShaftCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            CenterCarHorizontally();
        }

        private void CenterCarHorizontally()
        {
            double w = ShaftCanvas.ActualWidth;
            if (w > 0)
                Canvas.SetLeft(ElevatorCar, (w - ElevatorCar.Width) / 2.0);
        }

        // ══════════════════════════════════════════════════════
        // 도어 애니메이션
        // ══════════════════════════════════════════════════════
        private void AnimateDoorOpen()
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                // 내부 페이드인
                var fadeIn = new DoubleAnimation(1.0, new Duration(TimeSpan.FromMilliseconds(400)));
                InteriorGrid.BeginAnimation(OpacityProperty, fadeIn);

                // 도어뷰 슬라이드
                double doorW = DoorLeft.ActualWidth > 0 ? DoorLeft.ActualWidth : 150;
                var leftAnim = new DoubleAnimation(-doorW, new Duration(TimeSpan.FromMilliseconds(800)))
                    { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } };
                var rightAnim = new DoubleAnimation(doorW, new Duration(TimeSpan.FromMilliseconds(800)))
                    { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } };
                DoorLeftTransform.BeginAnimation(TranslateTransform.XProperty, leftAnim);
                DoorRightTransform.BeginAnimation(TranslateTransform.XProperty, rightAnim);

                // 가운데 선 숨기기
                DoorGap.Visibility    = Visibility.Hidden;
                CarDoorGap.Visibility = Visibility.Hidden;

                // 샤프트 카 도어 슬라이드
                var carLeftAnim = new DoubleAnimation(-26, new Duration(TimeSpan.FromMilliseconds(500)))
                    { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } };
                var carRightAnim = new DoubleAnimation(26, new Duration(TimeSpan.FromMilliseconds(500)))
                    { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } };
                CarDoorLeftTx.BeginAnimation(TranslateTransform.XProperty, carLeftAnim);
                CarDoorRightTx.BeginAnimation(TranslateTransform.XProperty, carRightAnim);
            }));
        }

        private void AnimateDoorClose()
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                // 내부 페이드아웃
                var fadeOut = new DoubleAnimation(0.0, new Duration(TimeSpan.FromMilliseconds(300)));
                InteriorGrid.BeginAnimation(OpacityProperty, fadeOut);

                // 도어뷰 닫기
                var leftAnim = new DoubleAnimation(0.0, new Duration(TimeSpan.FromMilliseconds(700)))
                    { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } };
                var rightAnim = new DoubleAnimation(0.0, new Duration(TimeSpan.FromMilliseconds(700)))
                    { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } };
                DoorLeftTransform.BeginAnimation(TranslateTransform.XProperty, leftAnim);
                DoorRightTransform.BeginAnimation(TranslateTransform.XProperty, rightAnim);

                // 도어가 완전히 닫힐 때쯤 가운데 선 다시 표시
                var gapTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
                gapTimer.Tick += (s, ev) =>
                {
                    gapTimer.Stop();
                    DoorGap.Visibility    = Visibility.Visible;
                    CarDoorGap.Visibility = Visibility.Visible;
                };
                gapTimer.Start();

                // 샤프트 카 도어 닫기
                var carLeftClose = new DoubleAnimation(0.0, new Duration(TimeSpan.FromMilliseconds(500)))
                    { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } };
                var carRightClose = new DoubleAnimation(0.0, new Duration(TimeSpan.FromMilliseconds(500)))
                    { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } };
                CarDoorLeftTx.BeginAnimation(TranslateTransform.XProperty, carLeftClose);
                CarDoorRightTx.BeginAnimation(TranslateTransform.XProperty, carRightClose);
            }));
        }

        // ══════════════════════════════════════════════════════
        // PLC 통신
        // ══════════════════════════════════════════════════════
        private async Task ConnectPlcAsync()
        {
            UpdatePlcStatus(connecting: true);
            uint result = await Task.Run(() => _plc.Connect("192.168.1.201", 2004));
            _plcConnected = (result == (uint)XGCOMM_FUNC_RESULT.RT_XGCOMM_SUCCESS);
            UpdatePlcStatus();
            if (_plcConnected) _keepAliveTimer.Start();
        }

        private async void KeepAliveTimer_Tick(object sender, EventArgs e)
        {
            uint result = await Task.Run(() => _plc.UpdateKeepAlive());
            bool wasConnected = _plcConnected;
            _plcConnected = (result == (uint)XGCOMM_FUNC_RESULT.RT_XGCOMM_SUCCESS);
            if (wasConnected != _plcConnected)
            {
                UpdatePlcStatus();
                if (!_plcConnected) _ = ConnectPlcAsync(); // 자동 재연결
            }
        }

        private void WritePlcBit(long bitOffset, byte value)
        {
            if (!_plcConnected) return;
            byte[] data = new byte[] { value };
            Task.Run(() => _plc.WriteDataBit('M', bitOffset, 1, data));
        }

        private void UpdatePlcStatus(bool connecting = false)
        {
            if (connecting)
            {
                PlcStatusText.Text       = "PLC: 연결 중...";
                PlcStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0x44));
            }
            else if (_plcConnected)
            {
                PlcStatusText.Text       = "PLC: 연결됨";
                PlcStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0x44, 0xDD, 0x66));
            }
            else
            {
                PlcStatusText.Text       = "PLC: 연결 끊김";
                PlcStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x44, 0x44));
            }
        }

        private static long GetFloorButtonBit(int floor) => 99 + floor; // 1F→100, 5F→104

        private static long GetCallButtonBit(int floor, string direction)
        {
            switch (floor)
            {
                case 1: return 107;                               // 1F ▲ only
                case 2: return direction == "Up" ? 108 : 109;
                case 3: return direction == "Up" ? 110 : 111;
                case 4: return direction == "Up" ? 112 : 113;
                case 5: return 114;                               // 5F ▼ only
                default: return -1;
            }
        }

        // ══════════════════════════════════════════════════════
        // 버튼 이벤트
        // ══════════════════════════════════════════════════════
        private void OpenDoor_MouseDown(object sender, MouseButtonEventArgs e) => WritePlcBit(105, 1);
        private void OpenDoor_MouseUp(object sender, MouseButtonEventArgs e)   => WritePlcBit(105, 0);
        private void CloseDoor_MouseDown(object sender, MouseButtonEventArgs e) => WritePlcBit(106, 1);
        private void CloseDoor_MouseUp(object sender, MouseButtonEventArgs e)   => WritePlcBit(106, 0);

        private void OpenDoor_Click(object sender, RoutedEventArgs e)
        {
            _doorTimer.Stop();
            _logic.TriggerDoorOpen();
            _doorTimer.Interval = TimeSpan.FromMilliseconds(7000);
            _doorTimer.Start();
            _doorTimer.Interval = TimeSpan.FromMilliseconds(5000);
        }

        private void CloseDoor_Click(object sender, RoutedEventArgs e)
        {
            _doorTimer.Stop();
            _logic.TriggerDoorClose();
            _processTimer.Start();
        }
    }
}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace ElevatorPanel
{
    public partial class MainWindow : Window
    {
        // 현재 엘리베이터 상태
        private int _currentFloor = 1;
        private enum Direction { Idle, Up, Down }
        private Direction _currentDirection = Direction.Idle;

        public MainWindow()
        {
            InitializeComponent();
        }

        // ===================== 층 버튼 클릭 =====================
        private void FloorButton_Click(object sender, RoutedEventArgs e)
        {
            Button btn = sender as Button;
            if (btn == null) return;

            int targetFloor = int.Parse(btn.Tag.ToString());
            MoveToFloor(targetFloor);
        }

        // ===================== 위 화살표 버튼 =====================
        private void ArrowUp_Click(object sender, RoutedEventArgs e)
        {
            Button btn = sender as Button;
            if (btn == null) return;

            int floor = int.Parse(btn.Tag.ToString());
            SetLedOn(floor, Direction.Up);
            _currentDirection = Direction.Up;
            UpdateDirectionIndicator();
        }

        // ===================== 아래 화살표 버튼 =====================
        private void ArrowDown_Click(object sender, RoutedEventArgs e)
        {
            Button btn = sender as Button;
            if (btn == null) return;

            int floor = int.Parse(btn.Tag.ToString());
            SetLedOn(floor, Direction.Down);
            _currentDirection = Direction.Down;
            UpdateDirectionIndicator();
        }

        // ===================== 열림 버튼 =====================
        private void DoorOpen_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(_currentFloor + "층 문 열림", "문 제어",
                            MessageBoxButton.OK, MessageBoxImage.Information);
            // TODO: PLC WriteDataBit 호출
            // xgComm.WriteDataBit('M', offsetBit, 1, new byte[]{ 1 });
        }

        // ===================== 닫힘 버튼 =====================
        private void DoorClose_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(_currentFloor + "층 문 닫힘", "문 제어",
                            MessageBoxButton.OK, MessageBoxImage.Information);
            // TODO: PLC WriteDataBit 호출
            // xgComm.WriteDataBit('M', offsetBit, 1, new byte[]{ 0 });
        }

        // ===================== 엘리베이터 이동 =====================
        private void MoveToFloor(int targetFloor)
        {
            if (targetFloor > _currentFloor)
                _currentDirection = Direction.Up;
            else if (targetFloor < _currentFloor)
                _currentDirection = Direction.Down;
            else
                _currentDirection = Direction.Idle;

            _currentFloor = targetFloor;

            UpdateDirectionIndicator();
            ResetAllLeds();
        }

        // ===================== LED 켜기 =====================
        private void SetLedOn(int floor, Direction dir)
        {
            ResetAllLeds();

            Ellipse target = null;

            if (floor == 5 && dir == Direction.Down) target = Led5D;
            else if (floor == 4 && dir == Direction.Up) target = Led4U;
            else if (floor == 4 && dir == Direction.Down) target = Led4D;
            else if (floor == 3 && dir == Direction.Up) target = Led3U;
            else if (floor == 3 && dir == Direction.Down) target = Led3D;
            else if (floor == 2 && dir == Direction.Up) target = Led2U;
            else if (floor == 2 && dir == Direction.Down) target = Led2D;
            else if (floor == 1 && dir == Direction.Up) target = Led1U;

            if (target != null)
                target.Fill = new SolidColorBrush(Color.FromRgb(0xEE, 0x22, 0x22));
        }

        // ===================== LED 전체 초기화 =====================
        private void ResetAllLeds()
        {
            SolidColorBrush gray = new SolidColorBrush(Color.FromRgb(0x77, 0x77, 0x77));
            Led5D.Fill = gray;
            Led4U.Fill = gray;
            Led4D.Fill = gray;
            Led3U.Fill = gray;
            Led3D.Fill = gray;
            Led2U.Fill = gray;
            Led2D.Fill = gray;
            Led1U.Fill = gray;
        }

        // ===================== 방향 삼각형 업데이트 =====================
        private void UpdateDirectionIndicator()
        {
            SolidColorBrush activeColor = new SolidColorBrush(Color.FromRgb(0x44, 0xCC, 0x44));
            SolidColorBrush inactiveColor = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55));

            if (_currentDirection == Direction.Up)
            {
                TriUp.Fill = activeColor;
                TriDown.Fill = inactiveColor;
            }
            else if (_currentDirection == Direction.Down)
            {
                TriUp.Fill = inactiveColor;
                TriDown.Fill = activeColor;
            }
            else
            {
                TriUp.Fill = inactiveColor;
                TriDown.Fill = inactiveColor;
            }
        }
    }
}

using System;
using System.Collections.Generic;

namespace ElevatorPLC
{
    // ── 층 정보 ─────────────────────────────────────────────
    public class FloorInfo
    {
        public int Number { get; set; }
        public string Name { get; set; }
        public bool WarmBg { get; set; }
    }

    // ── PLC 호출 주소 쌍 (nullable 대신 string 그대로) ──────
    public class CallAddrPair
    {
        public string Up { get; set; }   // null 이면 해당 방향 버튼 없음
        public string Down { get; set; }
    }

    // ── 엘리베이터 상태 ─────────────────────────────────────
    public enum ElevatorState { Idle, Moving, DoorOpen }

    // ── 이벤트 인자 ─────────────────────────────────────────
    public class ElevatorEventArgs : EventArgs
    {
        public int CurrentFloor { get; set; }
        public int Direction { get; set; }  // +1 상승 / -1 하강 / 0 정지
        public ElevatorState State { get; set; }
        public List<int> Queue { get; set; }

        public ElevatorEventArgs()
        {
            Queue = new List<int>();
        }
    }

    // ── 엘리베이터 로직 클래스 ──────────────────────────────
    public class ElevatorLogic
    {
        // 층 정보 (6층 → 1층 순)
        public static readonly List<FloorInfo> Floors = new List<FloorInfo>
        {
            new FloorInfo { Number = 5, Name = "PC방",           WarmBg = false },
            new FloorInfo { Number = 4, Name = "엽기떡볶이",   WarmBg = true  },
            new FloorInfo { Number = 3, Name = "노래방",     WarmBg = false },
            new FloorInfo { Number = 2, Name = "카페", WarmBg = false },
            new FloorInfo { Number = 1, Name = "로비",           WarmBg = false },
        };

        // 층 센서 PLC 주소
        public static readonly Dictionary<int, string> FloorSensorAddr =
            new Dictionary<int, string>
            {
                { 5, "P14" }, { 4, "P13" },
                { 3, "P12" }, { 2, "P11" }, { 1, "P10" }
            };

        // 호출 버튼 PLC 주소
        public static readonly Dictionary<int, CallAddrPair> CallAddr =
            new Dictionary<int, CallAddrPair>
            {
                { 5, new CallAddrPair { Up = null,  Down = "POD" } },
                { 4, new CallAddrPair { Up = "POC", Down = "POB" } },
                { 3, new CallAddrPair { Up = "P0A", Down = "P09" } },
                { 2, new CallAddrPair { Up = "P08", Down = "P07" } },
                { 1, new CallAddrPair { Up = "P06", Down = null  } },
            };

        // ── 상태 프로퍼티 ────────────────────────────────────
        public int CurrentFloor { get; private set; }
        public int Direction { get; private set; }
        public ElevatorState State { get; private set; }
        public List<int> Queue { get; private set; }
        public bool DoorOpen { get; private set; }
        public bool HasPending { get { return Queue.Count > 0; } }

        // ── 이벤트 ──────────────────────────────────────────
        public event EventHandler<ElevatorEventArgs> StateChanged;
        public event EventHandler DoorOpened;
        public event EventHandler DoorClosed;
        public event EventHandler FloorArrived;

        public ElevatorLogic()
        {
            CurrentFloor = 1;
            Direction = 0;
            State = ElevatorState.Idle;
            Queue = new List<int>();
            DoorOpen = false;
        }

        // ── 층 요청 ──────────────────────────────────────────
        public void RequestFloor(int floor)
        {
            if (floor == CurrentFloor && State != ElevatorState.Moving)
            {
                TriggerDoorOpen();
                return;
            }
            if (!Queue.Contains(floor))
                Queue.Add(floor);
            NotifyStateChanged();
        }

        // ── 한 칸 이동 (DispatcherTimer 틱마다 호출) ─────────
        public bool StepMove()
        {
            if (State != ElevatorState.Moving || Queue.Count == 0)
                return false;

            CurrentFloor += Direction;
            NotifyStateChanged();

            if (CurrentFloor == Queue[0])
            {
                Queue.RemoveAt(0);
                State = ElevatorState.Idle;
                Direction = 0;

                if (FloorArrived != null)
                    FloorArrived(this, EventArgs.Empty);

                // 도어 열기는 호출자(MainWindow)가 딜레이 후 직접 호출
                NotifyStateChanged();
                return true;
            }
            return false;
        }

        // ── 이동 시작 ────────────────────────────────────────
        public bool StartMoving()
        {
            if (State == ElevatorState.Moving || Queue.Count == 0)
                return false;

            if (Queue[0] == CurrentFloor)
            {
                Queue.RemoveAt(0);
                TriggerDoorOpen();
                return false;
            }

            if (DoorOpen) TriggerDoorClose();

            State = ElevatorState.Moving;
            Direction = Queue[0] > CurrentFloor ? 1 : -1;
            NotifyStateChanged();
            return true;
        }

        // ── 도어 열기 ────────────────────────────────────────
        public void TriggerDoorOpen()
        {
            DoorOpen = true;
            State = ElevatorState.DoorOpen;

            if (DoorOpened != null)
                DoorOpened(this, EventArgs.Empty);

            NotifyStateChanged();
        }

        // ── 도어 닫기 ────────────────────────────────────────
        public void TriggerDoorClose()
        {
            DoorOpen = false;
            State = ElevatorState.Idle;

            if (DoorClosed != null)
                DoorClosed(this, EventArgs.Empty);

            NotifyStateChanged();
        }

        // ── 상태 변경 알림 ───────────────────────────────────
        private void NotifyStateChanged()
        {
            if (StateChanged != null)
            {
                StateChanged(this, new ElevatorEventArgs
                {
                    CurrentFloor = CurrentFloor,
                    Direction = Direction,
                    State = State,
                    Queue = new List<int>(Queue),
                });
            }
        }
    }
}

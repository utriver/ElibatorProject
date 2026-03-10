# ElibatorProject

5층 건물 엘리베이터의 HMI(Human-Machine Interface)를 WPF로 구현하고, LS Electric XGT 시리즈 PLC와 연동하는 프로젝트입니다.

---

## 주요 기능

- **엘리베이터 시뮬레이션**: 5개 층 이동, 문 열림/닫힘, 방향 표시 등 실제 엘리베이터 동작 재현
- **PLC 연동**: XGCommLib COM 라이브러리를 통해 LS Electric XGT PLC와 TCP/IP 통신
- **3분할 UI**:
  - 왼쪽 — 샤프트뷰: 층별 상태 및 카(Car) 위치 애니메이션
  - 가운데 — 도어뷰: 문 슬라이드 애니메이션, 내부 배경 페이드인/아웃
  - 오른쪽 — 컨트롤패널: 층 버튼, 층 안내판, 문 열기/닫기
- **자동 재연결**: PLC 연결 끊김 감지 시 자동으로 재연결 시도

---

## 프로젝트 구조

```
ElibatorProject/
└── HMI-UI/                     # WPF 프로젝트 (ElevatorPLC)
    ├── ElevatorPLC.csproj      # 프로젝트 파일 (.NET Framework 4.8)
    ├── App.xaml / App.xaml.cs  # 앱 진입점
    ├── MainWindow.xaml         # UI 레이아웃 (XAML)
    ├── MainWindow.xaml.cs      # UI 코드비하인드 (타이머, PLC 통신, 애니메이션)
    ├── ElevatorLogic.cs        # 엘리베이터 상태 머신 및 층 요청 큐 관리
    └── XGCommLib.cs            # PLC 통신 COM 래퍼 (XGCommSocket)
```

---

## 아키텍처

### ElevatorLogic (상태 머신)

`ElevatorState` 열거형을 기반으로 동작합니다.

| 상태 | 설명 |
|------|------|
| `Idle` | 대기 중 |
| `Moving` | 층 간 이동 중 |
| `DoorOpen` | 문 열림 |

주요 흐름:
1. 버튼 입력 → `RequestFloor()` → 큐(Queue)에 목적층 추가
2. `StartMoving()` → 방향 결정 후 `Moving` 상태 전환
3. `MoveTimer` 틱(2초)마다 `StepMove()` 호출 → 한 층씩 이동
4. 목적층 도착 → 1초 딜레이 후 `TriggerDoorOpen()`
5. `DoorTimer`(5초) 만료 → `TriggerDoorClose()` → 다음 대기 층 처리

### PLC 통신

| 항목 | 값 |
|------|----|
| IP | 192.168.1.201 |
| Port | 2004 |
| 통신 방식 | TCP/IP (XGCommSocket) |
| Keep-alive 주기 | 1초 |

#### PLC 주소 맵

**층 센서 (입력)**

| 층 | 주소 |
|----|------|
| 1층 | P10 |
| 2층 | P11 |
| 3층 | P12 |
| 4층 | P13 |
| 5층 | P14 |

**호출 버튼 (출력, M-디바이스 비트)**

| 층 | 상행(▲) | 하행(▼) |
|----|---------|---------|
| 1층 | M107 | — |
| 2층 | M108 | M109 |
| 3층 | M110 | M111 |
| 4층 | M112 | M113 |
| 5층 | — | M114 |

**내부 버튼 (출력)**

| 기능 | 주소 |
|------|------|
| 1층 버튼 | M100 |
| 2층 버튼 | M101 |
| 3층 버튼 | M102 |
| 4층 버튼 | M103 |
| 5층 버튼 | M104 |
| 문 열기 | M105 |
| 문 닫기 | M106 |

---

## 층 구성

| 층 | 이름 |
|----|------|
| 5층 | PC방 |
| 4층 | 엽기떡볶이 |
| 3층 | 노래방 |
| 2층 | 카페 |
| 1층 | 로비 |

---

## 개발 환경

| 항목 | 내용 |
|------|------|
| 언어 | C# |
| 프레임워크 | .NET Framework 4.8 |
| UI | WPF (Windows Presentation Foundation) |
| IDE | Visual Studio |
| PLC 라이브러리 | XGCommLib (LS Electric COM, `tlbimp` 래퍼) |

---

## 빌드 및 실행

1. Visual Studio에서 `HMI-UI/ElevatorPLC.csproj` 열기
2. XGCommLib COM 컴포넌트가 등록되어 있는지 확인
3. `Debug` 또는 `Release`로 빌드
4. PLC가 `192.168.1.201:2004`에서 접근 가능한 네트워크에 연결된 상태로 실행
   - PLC 없이 실행해도 HMI 시뮬레이션은 동작하며, 상태바에 "PLC: 연결 끊김" 표시

---

## 타이머 구성

| 타이머 | 주기 | 역할 |
|--------|------|------|
| `_moveTimer` | 2,000ms | 층 간 이동 (한 틱 = 한 층) |
| `_doorTimer` | 5,000ms | 문 열림 유지 후 자동 닫힘 |
| `_processTimer` | 600ms | 문 닫힘 후 다음 목적층 처리 |
| `_keepAliveTimer` | 1,000ms | PLC 연결 유지 및 상태 확인 |

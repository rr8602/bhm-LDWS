# ECU Test Logger Pattern

다른 프로젝트에서도 동일한 패턴으로 TX/RX 로그 파일을 생성하기 위한 가이드입니다.

---

## 1. 폴더 구조

```
Logs\
  └── yyyy\
      └── MM\
          └── dd\
              ├── {WorkflowName}_{HHmmss}.log
              └── ...
```

**예시:**
```
Logs\
  └── 2026\
      └── 03\
          └── 06\
              ├── Camera_StaticCalibration_EOL_143025.log
              ├── Camera_DynamicCalibration_144530.log
              └── Radar_EOL_Test_150012.log
```

---

## 2. 로그 파일 형식

```
================================================================================
  Workflow    : Camera_StaticCalibration_EOL
  ECU Type    : Camera
  Start Time  : 2026-03-06 14:30:25.123
================================================================================

  Time           | Dir | CAN ID     | Data (HEX)                        | Description
  ---------------+-----+------------+-----------------------------------+---------------------------

  [14:30:25.150] ===== Step 1: Extended Session + Vehicle Data Write + Clear DTC =====

  14:30:25.160   | TX >> | 0x18DAE8F0 | 02 10 03 00 00 00 00 00           | DiagSessionControl (0x03)
  14:30:25.185   | RX << | 0x18DAF0E8 | 02 50 03 00 00 00 00 00           | DiagSessionControl +RSP
  14:30:25.200   | TX >> | 0x18DAE8F0 | 10 11 2E FD 10 00 64 00           | First Frame (Multi)
  14:30:25.225   | RX << | 0x18DAF0E8 | 30 00 00 00 00 00 00 00           | Flow Control
  14:30:25.280   | TX >> | 0x18DAE8F0 | 21 64 00 50 00 00 00 00           | Consecutive Frame (SN=1)
  14:30:25.320   | RX << | 0x18DAF0E8 | 03 6E FD 10 00 00 00 00           | WriteDataByID +RSP

  [14:30:26.100] ===== Step 2: ECU Reset =====

  14:30:26.110   | TX >> | 0x18DAE8F0 | 02 11 01 00 00 00 00 00           | ECUReset
  14:30:26.150   | RX << | 0x18DAF0E8 | 02 51 01 00 00 00 00 00           | ECUReset +RSP

  14:35:30.400   | ERR | ---------- | *** ERROR: Timeout waiting for response ***

================================================================================
  End Time    : 2026-03-06 14:35:30.456
  Duration    : 305.333 seconds
  Result      : SUCCESS (또는 FAILED)
  Error       : (실패 시 에러 메시지)
================================================================================
```

---

## 3. Logger 클래스 구현

### 3.1 Logger.cs

```csharp
using System;
using System.IO;
using System.Text;

namespace YourNamespace
{
    public enum EcuType
    {
        Camera,
        Radar
        // 필요시 추가
    }

    public class Logger : IDisposable
    {
        private StreamWriter _writer;
        private string _logFilePath;
        private string _workflowName;
        private DateTime _startTime;
        private bool _disposed = false;
        private readonly object _lock = new object();

        // 기본 로그 폴더 (실행 파일 위치 기준)
        private static readonly string BaseLogFolder = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "Logs");

        public string LogFilePath => _logFilePath;

        /// <summary>
        /// 워크플로우 로깅 세션 시작 (ECU 타입 지정)
        /// </summary>
        public void StartSession(string workflowName, EcuType ecuType)
        {
            StartSessionInternal(workflowName, ecuType.ToString());
        }

        /// <summary>
        /// 워크플로우 로깅 세션 시작 (전체 세션용 - 모든 ECU 테스트 추적)
        /// </summary>
        public void StartSession(string sessionName)
        {
            StartSessionInternal(sessionName, "All (Camera & Radar)");
        }

        private void StartSessionInternal(string workflowName, string ecuTypeStr)
        {
            _workflowName = workflowName;
            _startTime = DateTime.Now;

            // 폴더 생성: Logs\yyyy\MM\dd
            string dateFolder = Path.Combine(
                BaseLogFolder,
                _startTime.ToString("yyyy"),
                _startTime.ToString("MM"),
                _startTime.ToString("dd"));

            Directory.CreateDirectory(dateFolder);

            // 파일명: {워크플로우명}_{HHmmss}.log
            string fileName = $"{workflowName}_{_startTime:HHmmss}.log";
            _logFilePath = Path.Combine(dateFolder, fileName);

            _writer = new StreamWriter(_logFilePath, false, Encoding.UTF8);
            _writer.AutoFlush = true;

            WriteHeader(ecuTypeStr);
        }

        private void WriteHeader(string ecuTypeStr)
        {
            var sb = new StringBuilder();
            sb.AppendLine("================================================================================");
            sb.AppendLine($"  Session     : {_workflowName}");
            sb.AppendLine($"  ECU Type    : {ecuTypeStr}");
            sb.AppendLine($"  Start Time  : {_startTime:yyyy-MM-dd HH:mm:ss.fff}");
            sb.AppendLine("================================================================================");
            sb.AppendLine();
            sb.AppendLine("  Time           | Dir | CAN ID     | Data (HEX)                        | Description");
            sb.AppendLine("  ---------------+-----+------------+-----------------------------------+---------------------------");

            lock (_lock)
            {
                _writer?.WriteLine(sb.ToString());
            }
        }

        /// <summary>
        /// TX (송신) 로그 기록
        /// </summary>
        public void LogTx(uint canId, byte[] data, string description = "")
        {
            LogMessage("TX", canId, data, description);
        }

        /// <summary>
        /// RX (수신) 로그 기록
        /// </summary>
        public void LogRx(uint canId, byte[] data, string description = "")
        {
            LogMessage("RX", canId, data, description);
        }

        /// <summary>
        /// 일반 정보 로그
        /// </summary>
        public void LogInfo(string message)
        {
            string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            string line = $"  {timestamp}   | --- | ---------- | {message}";

            lock (_lock)
            {
                _writer?.WriteLine(line);
            }
        }

        /// <summary>
        /// 에러 로그
        /// </summary>
        public void LogError(string message)
        {
            string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            string line = $"  {timestamp}   | ERR | ---------- | *** ERROR: {message} ***";

            lock (_lock)
            {
                _writer?.WriteLine(line);
            }
        }

        /// <summary>
        /// 단계 구분자 로그
        /// </summary>
        public void LogStep(string stepName)
        {
            string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");

            lock (_lock)
            {
                _writer?.WriteLine();
                _writer?.WriteLine($"  [{timestamp}] ===== {stepName} =====");
                _writer?.WriteLine();
            }
        }

        private void LogMessage(string direction, uint canId, byte[] data, string description)
        {
            if (_writer == null) return;

            string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            string dirArrow = direction == "TX" ? ">>" : "<<";
            string hexData = FormatHexData(data);
            string descText = string.IsNullOrEmpty(description)
                ? ParseUdsDescription(data, direction)
                : description;

            string line = $"  {timestamp}   | {direction} {dirArrow} | 0x{canId:X8} | {hexData,-33} | {descText}";

            lock (_lock)
            {
                _writer?.WriteLine(line);
            }
        }

        private string FormatHexData(byte[] data)
        {
            if (data == null || data.Length == 0) return "";

            var sb = new StringBuilder();
            for (int i = 0; i < data.Length && i < 8; i++)
            {
                if (i > 0) sb.Append(" ");
                sb.Append(data[i].ToString("X2"));
            }
            return sb.ToString();
        }

        /// <summary>
        /// UDS 메시지 자동 설명 생성 (프로젝트에 맞게 수정)
        /// </summary>
        private string ParseUdsDescription(byte[] data, string direction)
        {
            if (data == null || data.Length < 2) return "";

            int pci = data[0];

            // Multi-frame 처리
            if ((pci & 0xF0) == 0x10) return "First Frame (Multi)";
            if ((pci & 0xF0) == 0x20) return $"Consecutive Frame (SN={pci & 0x0F})";
            if (pci == 0x30) return "Flow Control";

            byte sid = data[1];

            // TX (Request)
            if (direction == "TX")
            {
                switch (sid)
                {
                    case 0x10: return $"DiagSessionControl (0x{data[2]:X2})";
                    case 0x11: return "ECUReset";
                    case 0x14: return "ClearDTC";
                    case 0x19: return "ReadDTCInfo";
                    case 0x22: return $"ReadDataByID (0x{data[2]:X2}{data[3]:X2})";
                    case 0x27: return data[2] == 0x01 ? "SecurityAccess (Seed Req)" : "SecurityAccess (Key Send)";
                    case 0x2E: return $"WriteDataByID (0x{data[2]:X2}{data[3]:X2})";
                    case 0x31: return $"RoutineControl (0x{data[3]:X2}{data[4]:X2})";
                    case 0x3E: return "TesterPresent";
                    default: return $"SID 0x{sid:X2}";
                }
            }
            // RX (Response)
            else
            {
                if (sid == 0x7F)
                    return $"Negative Response (NRC=0x{data[3]:X2})";

                switch (sid)
                {
                    case 0x50: return "DiagSessionControl +RSP";
                    case 0x51: return "ECUReset +RSP";
                    case 0x54: return "ClearDTC +RSP";
                    case 0x59: return "ReadDTCInfo +RSP";
                    case 0x62: return "ReadDataByID +RSP";
                    case 0x67: return "SecurityAccess +RSP";
                    case 0x6E: return "WriteDataByID +RSP";
                    case 0x71: return "RoutineControl +RSP";
                    case 0x7E: return "TesterPresent +RSP";
                    default: return $"RSP 0x{sid:X2}";
                }
            }
        }

        /// <summary>
        /// 워크플로우 로깅 세션 종료
        /// </summary>
        public void EndSession(bool success, string errorMessage = "")
        {
            if (_writer == null) return;

            DateTime endTime = DateTime.Now;
            TimeSpan duration = endTime - _startTime;

            var sb = new StringBuilder();
            sb.AppendLine();
            sb.AppendLine("================================================================================");
            sb.AppendLine($"  End Time    : {endTime:yyyy-MM-dd HH:mm:ss.fff}");
            sb.AppendLine($"  Duration    : {duration.TotalSeconds:F3} seconds");
            sb.AppendLine($"  Result      : {(success ? "SUCCESS" : "FAILED")}");
            if (!success && !string.IsNullOrEmpty(errorMessage))
            {
                sb.AppendLine($"  Error       : {errorMessage}");
            }
            sb.AppendLine("================================================================================");

            lock (_lock)
            {
                _writer?.WriteLine(sb.ToString());
                _writer?.Flush();
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;

            if (disposing)
            {
                lock (_lock)
                {
                    _writer?.Dispose();
                    _writer = null;
                }
            }

            _disposed = true;
        }

        ~Logger()
        {
            Dispose(false);
        }
    }
}
```

---

## 4. 통신 클라이언트에 Logger 연동

### 4.1 클라이언트 클래스에 Logger 프로퍼티 추가

```csharp
public class UDSClient
{
    private Logger _logger;
    public Logger Logger
    {
        get => _logger;
        set => _logger = value;
    }

    // ... 기존 코드 ...
}
```

### 4.2 TX/RX 시점에 로깅 호출

```csharp
// 송신 (TX) 시
public void SendMessage(byte[] data)
{
    // ... 실제 전송 코드 ...

    _logger?.LogTx(canId, frameData);
}

// 수신 (RX) 시
public byte[] ReceiveMessage()
{
    // ... 실제 수신 코드 ...

    _logger?.LogRx(canId, rxData);
    return rxData;
}
```

### 4.3 워크플로우 메서드에 단계 로깅 추가

```csharp
public void SomeWorkflow()
{
    _logger?.LogStep("Step 1: Extended Session");
    EnterExtendedSession();

    _logger?.LogStep("Step 2: Security Access");
    SecurityAccess();

    // ...
}
```

---

## 5. UI에서 Logger 사용

### 5.1 방식 A: 앱 시작/종료 시 자동 로깅 (권장)

모든 테스트 행위(자동/수동)를 하나의 로그 파일에 기록합니다.

```csharp
private Logger _logger;

public MainWindow()
{
    InitializeComponent();

    _client = new UDSClient();

    // 앱 시작 시 자동으로 로깅 세션 시작
    _logger = new Logger();
    _logger.StartSession("ECU_Test_Session");  // ECU 타입 없이 전체 세션
    _client.Logger = _logger;
}

protected override void OnClosed(EventArgs e)
{
    // 앱 종료 시 자동으로 로깅 세션 종료
    if (_logger != null)
    {
        _logger.EndSession(true);
        _logger.Dispose();
    }

    _client?.Dispose();
    base.OnClosed(e);
}

// 버튼 핸들러에서는 LogStep만 호출
private async void btnRunWorkflow_Click(object sender, RoutedEventArgs e)
{
    _logger?.LogStep("[Camera] Static Calibration - Start");

    try
    {
        await Task.Run(() => _client.RunWorkflow());
        _logger?.LogInfo("[Camera] Static Calibration - Completed");
    }
    catch (Exception ex)
    {
        _logger?.LogError(ex.Message);
    }
}
```

### 5.2 방식 B: 워크플로우별 개별 로그 파일

각 워크플로우마다 별도의 로그 파일을 생성합니다.

```csharp
private async void btnRunWorkflow_Click(object sender, RoutedEventArgs e)
{
    // 1. 로거 초기화 및 세션 시작
    var logger = new Logger();
    logger.StartSession("WorkflowName", EcuType.Camera);
    _client.Logger = logger;

    bool success = false;
    string errorMsg = "";

    try
    {
        await Task.Run(() => _client.RunWorkflow());
        success = true;
    }
    catch (Exception ex)
    {
        errorMsg = ex.Message;
        logger.LogError(ex.Message);
    }
    finally
    {
        // 세션 종료 및 정리
        logger.EndSession(success, errorMsg);
        logger.Dispose();
        _client.Logger = null;
    }
}
```

---

## 6. 체크리스트

새 프로젝트에 Logger를 적용할 때:

- [ ] `Logger.cs` 파일 복사 및 namespace 수정
- [ ] `EcuType` enum을 프로젝트에 맞게 수정
- [ ] 통신 클라이언트에 `Logger` 프로퍼티 추가
- [ ] TX 시점에 `_logger?.LogTx(canId, data)` 호출
- [ ] RX 시점에 `_logger?.LogRx(canId, data)` 호출
- [ ] 워크플로우 메서드에 `_logger?.LogStep("단계명")` 추가
- [ ] UI 버튼 핸들러에서 `StartSession()` / `EndSession()` 호출
- [ ] `ParseUdsDescription()` 메서드를 프로젝트 프로토콜에 맞게 수정
- [ ] `.csproj`에 `Logger.cs` 추가

---

## 7. 참고사항

- 로그 파일은 `{실행파일경로}\Logs\yyyy\MM\dd\` 에 저장됨
- 파일명 형식: `{WorkflowName}_{HHmmss}.log`
- Thread-safe 설계 (lock 사용)
- `IDisposable` 구현으로 리소스 관리
- `_logger?.` null 조건 연산자로 로거 미설정 시에도 안전하게 동작

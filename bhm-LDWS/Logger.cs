using System;
using System.IO;
using System.Text;

namespace bhm_LDWS
{
    /// <summary>
    /// ECU 테스트 로그 파일 관리 클래스
    /// 폴더 구조: Logs\yyyy\MM\dd\{워크플로우명}_{HHmmss}.log
    /// </summary>
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

        /// <summary>
        /// 현재 로그 파일 경로
        /// </summary>
        public string LogFilePath => _logFilePath;

        /// <summary>
        /// 워크플로우 로깅 세션 시작 (ECU 타입 지정)
        /// </summary>
        /// <param name="workflowName">워크플로우 이름 (예: CameraStaticEOL, RadarEOL)</param>
        /// <param name="ecuType">ECU 타입</param>
        public void StartSession(string workflowName, EcuType ecuType)
        {
            StartSessionInternal(workflowName, ecuType.ToString());
        }

        /// <summary>
        /// 워크플로우 로깅 세션 시작 (전체 세션용 - 모든 ECU 테스트 추적)
        /// </summary>
        /// <param name="sessionName">세션 이름 (예: ECU_Test_Session)</param>
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

            // 헤더 작성
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
        /// <param name="canId">CAN ID</param>
        /// <param name="data">송신 데이터</param>
        /// <param name="description">설명 (선택)</param>
        public void LogTx(uint canId, byte[] data, string description = "")
        {
            LogMessage("TX", canId, data, description);
        }

        /// <summary>
        /// RX (수신) 로그 기록
        /// </summary>
        /// <param name="canId">CAN ID</param>
        /// <param name="data">수신 데이터</param>
        /// <param name="description">설명 (선택)</param>
        public void LogRx(uint canId, byte[] data, string description = "")
        {
            LogMessage("RX", canId, data, description);
        }

        /// <summary>
        /// 일반 메시지 로그 기록
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
        /// 에러 메시지 로그 기록
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
        /// 단계 구분자 로그 기록
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
            string descText = string.IsNullOrEmpty(description) ? ParseUdsDescription(data, direction) : description;

            // 고정 폭 포맷
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
        /// UDS 메시지 자동 설명 생성
        /// </summary>
        private string ParseUdsDescription(byte[] data, string direction)
        {
            if (data == null || data.Length < 2) return "";

            int pci = data[0];

            // Multi-frame 처리
            if ((pci & 0xF0) == 0x10) return "First Frame (Multi)";
            if ((pci & 0xF0) == 0x20) return $"Consecutive Frame (SN={pci & 0x0F})";
            if (pci == 0x30) return "Flow Control";

            // Single Frame
            if (data.Length < 2) return "";
            byte sid = data[1];

            // TX (Request)
            if (direction == "TX")
            {
                switch (sid)
                {
                    case 0x10: return data.Length > 2 ? $"DiagSessionControl (0x{data[2]:X2})" : "DiagSessionControl";
                    case 0x11: return "ECUReset";
                    case 0x14: return "ClearDTC";
                    case 0x19: return "ReadDTCInfo";
                    case 0x22: return data.Length > 3 ? $"ReadDataByID (0x{data[2]:X2}{data[3]:X2})" : "ReadDataByID";
                    case 0x27: return data.Length > 2 && data[2] == 0x01 ? "SecurityAccess (Seed Req)" : "SecurityAccess (Key Send)";
                    case 0x2E: return data.Length > 3 ? $"WriteDataByID (0x{data[2]:X2}{data[3]:X2})" : "WriteDataByID";
                    case 0x31:
                        string subFunc = data.Length > 2 ? (data[2] == 0x01 ? "Start" : (data[2] == 0x02 ? "Stop" : "Result")) : "";
                        return data.Length > 4 ? $"RoutineControl {subFunc} (0x{data[3]:X2}{data[4]:X2})" : $"RoutineControl {subFunc}";
                    case 0x3E: return "TesterPresent";
                    default: return $"SID 0x{sid:X2}";
                }
            }
            // RX (Response)
            else
            {
                if (sid == 0x7F)
                {
                    byte nrc = data.Length > 3 ? data[3] : (byte)0;
                    return $"Negative Response (NRC=0x{nrc:X2})";
                }

                switch (sid)
                {
                    case 0x50: return "DiagSessionControl +RSP";
                    case 0x51: return "ECUReset +RSP";
                    case 0x54: return "ClearDTC +RSP";
                    case 0x59: return "ReadDTCInfo +RSP";
                    case 0x62: return "ReadDataByID +RSP";
                    case 0x67: return data.Length > 2 && data[2] == 0x01 ? "SecurityAccess +RSP (Seed)" : "SecurityAccess +RSP (OK)";
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
        /// <param name="success">성공 여부</param>
        /// <param name="errorMessage">에러 메시지 (실패 시)</param>
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

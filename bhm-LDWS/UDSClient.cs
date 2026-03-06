using KI_RnB;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace bhm_LDWS
{
    public enum EcuType
    {
        Camera,
        Radar
    }

    public class UDSClient
    {
        #region Fields & Properties

        private IntPtr hObject = IntPtr.Zero;
        private bool _disposed = false;
        private Timer _heartbeatTimer;

        // Logger for TX/RX logging
        private Logger _logger;
        public Logger Logger
        {
            get => _logger;
            set => _logger = value;
        }

        // Camera CAN IDs (29-bit extended, J1939) - Security Access 문서 기준
        // "Request on CAN Id 0x18 DA E8 xx" → ECU=0xE8, Tester=0xF0
        private const uint CameraTesterID = 0x18DAE8F0;
        private const uint CameraEcuID = 0x18DAF0E8;

        // Radar CAN IDs (29-bit extended, J1939) - Y526316 문서 Page 2 기준
        // "if the tester source address is 0xF0, ECU source address is 0x2A and the priority is 6,
        //  the CAN identifier would be 0x18DA2AF0"
        private const uint RadarTesterID = 0x18DA2AF0;  // ECU=0x2A, Tester=0xF0
        private const uint RadarEcuID = 0x18DAF02A;     // Response: ECU→Tester

        private const int MaxFrameSize = 8;
        private byte sequenceNumber = 0x21;

        // 현재 통신 대상 ECU 타입
        // UI에서 버튼 클릭 시 명시적으로 설정됨 (예: _client.CurrentEcuType = EcuType.Radar)
        private EcuType _currentEcuType = EcuType.Camera;
        public EcuType CurrentEcuType
        {
            get => _currentEcuType;
            set => _currentEcuType = value;
        }

        #endregion

        #region Constructor & Dispose

        public UDSClient()
        {
            OpenDevice();
            _heartbeatTimer = new Timer(_ => SendHeartbeat(), null, Timeout.Infinite, Timeout.Infinite);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        public virtual void Dispose(bool disposing)
        {
            if (_disposed) return;

            if (disposing)
            {
                if (_heartbeatTimer != null)
                {
                    _heartbeatTimer.Dispose();
                    _heartbeatTimer = null;
                }
            }

            if (hObject != IntPtr.Zero)
            {
                int errors = 0;
                icsNeoDll.icsneoClosePort(hObject, ref errors);
                Console.WriteLine($"Port closed with {errors} errors");
                hObject = IntPtr.Zero;
            }

            _disposed = true;
        }

        ~UDSClient()
        {
            Dispose(false);
        }

        #endregion

        #region Device & Communication Core

        public void OpenDevice()
        {
            NeoDevice neoDevice = new NeoDevice();
            int numDevices = 1;
            uint deviceTypes = (uint)eHardwareTypes.NEODEVICE_ALL;

            if (icsNeoDll.icsneoFindNeoDevices(deviceTypes, ref neoDevice, ref numDevices) == 0)
            {
                throw new Exception("No neoVI device found");
            }

            byte[] networkIDs = new byte[16];
            int configRead = 1;
            int syncToPC = 1;

            if (icsNeoDll.icsneoOpenNeoDevice(ref neoDevice, ref hObject, ref networkIDs[0], configRead, syncToPC) == 0)
            {
                throw new Exception("Failed to open neoVI device");
            }

            icsNeoDll.icsneoEnableNetworkCom(hObject, 1);
            Console.WriteLine("Device opened successfully");
        }

        // 현재 ECU 타입에 따른 Tester CAN ID 반환
        private uint GetTesterID()
        {
            return _currentEcuType == EcuType.Camera ? CameraTesterID : RadarTesterID;
        }

        // 현재 ECU 타입에 따른 ECU Response CAN ID 반환
        private uint GetEcuID()
        {
            return _currentEcuType == EcuType.Camera ? CameraEcuID : RadarEcuID;
        }

        // UDS 명령어 전송 (Single / Multi-frame 지원)
        public byte[] SendUDSCommand(byte[] command, bool expectResponse = true)
        {
            uint testerID = GetTesterID();
            // Camera와 Radar 모두 29-bit extended (J1939) 사용
            bool isExtended = true;

            icsSpyMessage txMsg = new icsSpyMessage
            {
                ArbIDOrHeader = (int)testerID,
                NetworkID = (byte)eNETWORK_ID.NETID_HSCAN,
                Protocol = (byte)ePROTOCOL.SPY_PROTOCOL_CAN,
                StatusBitField = isExtended
                    ? (int)(eDATA_STATUS_BITFIELD_1.SPY_STATUS_TX_MSG | eDATA_STATUS_BITFIELD_1.SPY_STATUS_XTD_FRAME)
                    : (int)eDATA_STATUS_BITFIELD_1.SPY_STATUS_TX_MSG,
                NumberBytesData = (byte)command.Length
            };

            if (command.Length <= 7)
            {
                // Single Frame
                byte[] frame = new byte[MaxFrameSize];
                frame[0] = (byte)command.Length;
                Array.Copy(command, 0, frame, 1, command.Length);
                SetMessageData(ref txMsg, frame);
                icsNeoDll.icsneoTxMessages(hObject, ref txMsg, (int)eNETWORK_ID.NETID_HSCAN, 1);
                Console.WriteLine($"[{_currentEcuType}] TX Single: {BitConverter.ToString(frame)}");
                _logger?.LogTx(testerID, frame);
            }
            else
            {
                // First Frame
                byte[] ff = new byte[MaxFrameSize];
                ff[0] = (byte)(0x10 | ((command.Length >> 8) & 0x0F));
                ff[1] = (byte)(command.Length & 0xFF);
                Array.Copy(command, 0, ff, 2, 6);
                SetMessageData(ref txMsg, ff);
                icsNeoDll.icsneoTxMessages(hObject, ref txMsg, (int)eNETWORK_ID.NETID_HSCAN, 1);
                Console.WriteLine($"[{_currentEcuType}] TX FF: {BitConverter.ToString(ff)}");
                _logger?.LogTx(testerID, ff);

                // Flow Control 대기
                byte[] fc = ReceiveSingleFrame(2000);

                if (fc.Length == 0 || fc[0] != 0x30)
                    throw new Exception("Flow Control not received");

                // Consecutive Frames
                int offset = 6;
                sequenceNumber = 0x21;

                while (offset < command.Length)
                {
                    byte[] cf = new byte[MaxFrameSize];
                    cf[0] = sequenceNumber++;
                    int len = Math.Min(7, command.Length - offset);
                    Array.Copy(command, offset, cf, 1, len);
                    SetMessageData(ref txMsg, cf);
                    icsNeoDll.icsneoTxMessages(hObject, ref txMsg, (int)eNETWORK_ID.NETID_HSCAN, 1);
                    Console.WriteLine($"[{_currentEcuType}] TX CF: {BitConverter.ToString(cf)}");
                    _logger?.LogTx(testerID, cf);
                    offset += len;
                    if (sequenceNumber > 0x2F) sequenceNumber = 0x20;
                    Thread.Sleep(50);  // STmin 준수
                }
            }

            if (!expectResponse) return null;

            return ReceiveUDSResponse(command[0]);
        }

        // 단일 프레임 수신 (타임아웃 적용, ECU ID 필터링)
        public byte[] ReceiveSingleFrame(int timeoutMs = 2000)
        {
            uint expectedEcuID = GetEcuID();
            DateTime startTime = DateTime.Now;

            while ((DateTime.Now - startTime).TotalMilliseconds < timeoutMs)
            {
                int waitResult = icsNeoDll.icsneoWaitForRxMessagesWithTimeOut(hObject, 100);
                if (waitResult == 0) continue;

                icsSpyMessage rxMsg = new icsSpyMessage();
                int numMsgs = 1;
                int errors = 0;

                if (icsNeoDll.icsneoGetMessages(hObject, ref rxMsg, ref numMsgs, ref errors) == 0 || numMsgs == 0)
                    continue;

                // ECU ID 필터링 - 현재 ECU 타입에 맞는 응답만 처리
                uint receivedID = (uint)rxMsg.ArbIDOrHeader;
                if (receivedID != expectedEcuID)
                {
                    Console.WriteLine($"[{_currentEcuType}] Ignored message from 0x{receivedID:X8} (expected 0x{expectedEcuID:X8})");
                    continue;
                }

                byte[] data = GetMessageData(ref rxMsg);
                byte[] rxData = data.Take(rxMsg.NumberBytesData).ToArray();
                Console.WriteLine($"[{_currentEcuType}] RX: ArbID 0x{rxMsg.ArbIDOrHeader:X8}, Data {BitConverter.ToString(rxData)}");
                _logger?.LogRx(receivedID, rxData);

                return rxData;
            }

            Console.WriteLine($"[{_currentEcuType}] Receive timeout after {timeoutMs}ms");
            throw new TimeoutException($"No message received within {timeoutMs}ms");
        }

        // UDS 응답 수신 (Multi-frame 지원) - PCI/SID 제외, 순수 데이터만 반환
        public byte[] ReceiveUDSResponse(byte originalSid)
        {
            byte[] first = ReceiveSingleFrame(5000);
            if (first.Length == 0) throw new Exception("Empty response");

            if ((first[0] & 0xF0) == 0x10)  // First Frame (Multi-frame)
            {
                int respLen = ((first[0] & 0x0F) << 8) | first[1];
                List<byte> fullResp = new List<byte>(first.Skip(2));

                // Flow Control 전송
                byte[] fc = { 0x30, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
                SendUDSCommand(fc, false);
                Console.WriteLine($"[{_currentEcuType}] TX FC: 30 00 00");

                // Consecutive Frame 수신
                while (fullResp.Count < respLen)
                {
                    byte[] cf = ReceiveSingleFrame(2000);
                    if ((cf[0] & 0xF0) != 0x20) throw new Exception("Invalid Consecutive Frame");
                    fullResp.AddRange(cf.Skip(1).TakeWhile(b => b != 0x00));
                }

                byte[] response = fullResp.ToArray();
                byte responseSid = response[0];

                if (responseSid == 0x7F)
                    throw new Exception($"Negative Response: SID={response[1]:X2}, NRC={response[2]:X2}");

                if (responseSid != (originalSid + 0x40))
                    throw new Exception($"Invalid Positive Response SID (expected {originalSid + 0x40:X2})");

                return response.Skip(1).ToArray();
            }
            else  // Single Frame
            {
                int dataLen = first[0] & 0x0F;
                byte responseSid = first[1];

                if (responseSid == 0x7F)
                    throw new Exception($"Negative Response: SID={first[2]:X2}, NRC={first[3]:X2}");

                if (responseSid != (originalSid + 0x40))
                    throw new Exception($"Invalid Positive Response SID (expected {originalSid + 0x40:X2}, got {responseSid:X2})");

                return first.Skip(2).Take(dataLen - 1).ToArray();
            }
        }

        public void SetMessageData(ref icsSpyMessage msg, byte[] data)
        {
            msg.NumberBytesData = (byte)data.Length;
            if (data.Length > 0) msg.Data1 = data[0];
            if (data.Length > 1) msg.Data2 = data[1];
            if (data.Length > 2) msg.Data3 = data[2];
            if (data.Length > 3) msg.Data4 = data[3];
            if (data.Length > 4) msg.Data5 = data[4];
            if (data.Length > 5) msg.Data6 = data[5];
            if (data.Length > 6) msg.Data7 = data[6];
            if (data.Length > 7) msg.Data8 = data[7];
        }

        public byte[] GetMessageData(ref icsSpyMessage msg)
        {
            return new byte[]
            {
                msg.Data1, msg.Data2, msg.Data3, msg.Data4,
                msg.Data5, msg.Data6, msg.Data7, msg.Data8
            };
        }

        #endregion

        #region Common UDS Services (Camera & Radar 공통)

        // Extended Session 진입 (0x10 0x03)
        public void EnterExtendedSession()
        {
            SendUDSCommand(new byte[] { 0x10, 0x03 });
            Console.WriteLine($"[{_currentEcuType}] Extended Session entered");
        }

        // DTC Check (0x19 0x02)
        public byte[] CheckDTC()
        {
            byte[] dtcCmd = { 0x19, 0x02, 0xFF };
            byte[] response = SendUDSCommand(dtcCmd);
            Console.WriteLine($"[{_currentEcuType}] DTC Check completed - {response.Length} bytes received");
            return response;
        }

        // Clear DTC (0x14)
        public void ClearDTC()
        {
            SendUDSCommand(new byte[] { 0x14, 0xFF, 0xFF, 0xFF });
            Console.WriteLine($"[{_currentEcuType}] DTC cleared");
        }

        // ECU Reset (0x11 0x01)
        public void EcuReset()
        {
            SendUDSCommand(new byte[] { 0x11, 0x01 });
            Console.WriteLine($"[{_currentEcuType}] ECU Reset completed");
        }

        // Heartbeat (0x3E 0x80) - Tester Present
        public void SendHeartbeat()
        {
            byte[] hb = { 0x3E, 0x80 };  // SuppressPosRspMsgIndicationBit
            SendUDSCommand(hb, false);
            Console.WriteLine($"[{_currentEcuType}] Heartbeat sent");
        }

        // Heartbeat 타이머 시작/중지
        public void StartHeartbeat(int intervalMs = 5000)
        {
            _heartbeatTimer?.Change(0, intervalMs);
        }

        public void StopHeartbeat()
        {
            _heartbeatTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        }

        #endregion

        #region Camera ECU Methods (Y417067 문서 기준)
        // ============================================================================
        // Camera ECU: PAEB & LDWS
        // - CAN ID: 0x7E0 (Tester) / 0x7E8 (ECU) - 11-bit standard
        // - Security Access: Routine Control ($31)에 필요, DID Read/Write에는 불필요
        // - Security 알고리즘: 업체에 별도 의뢰 필요 (현재 미구현)
        // ============================================================================

        // Security Access (Camera용)
        // 알고리즘 (문서 6.3 Key calculation formula):
        //   KEY = (((SEED >> 16) | (SEED << 16)) % 0xC503U)
        // 상위/하위 16비트 swap 후 0xC503으로 modulo
        public void CameraSecurityAccess()
        {
            byte[] seedReq = { 0x27, 0x01 };
            byte[] seedResp = SendUDSCommand(seedReq);

            if (seedResp.Length < 5 || seedResp[0] != 0x01)
                throw new Exception($"Security Seed request failed: {BitConverter.ToString(seedResp)}");

            uint seed = BitConverter.ToUInt32(seedResp, 1);

            // Camera 알고리즘: KEY = (((SEED >> 16) | (SEED << 16)) % 0xC503U)
            uint swapped = (seed >> 16) | (seed << 16);  // 상위/하위 16비트 swap
            uint key = swapped % 0xC503U;

            Console.WriteLine($"[Camera] Security: Seed=0x{seed:X8}, Swapped=0x{swapped:X8}, Key=0x{key:X8}");

            byte[] keyBytes = BitConverter.GetBytes(key);
            byte[] keyReq = new byte[] { 0x27, 0x02 }.Concat(keyBytes).ToArray();
            byte[] keyResp = SendUDSCommand(keyReq);

            if (keyResp.Length < 1 || keyResp[0] != 0x02)
                throw new Exception($"Security Key failed: {BitConverter.ToString(keyResp)}");

            Console.WriteLine("[Camera] Security Access successful");
        }

        // DTC 체크 - 캘리브레이션 전 필수 DTC 확인 (0xA9060C, 0xA9060E가 없어야 함)
        public bool CameraCheckCalibrationDTC()
        {
            byte[] response = CheckDTC();

            if (response.Length < 1)
                return true;

            int dtcCount = (response.Length - 1) / 4;

            for (int i = 0; i < dtcCount; i++)
            {
                int offset = 1 + (i * 4);
                uint dtc = (uint)((response[offset] << 16) | (response[offset + 1] << 8) | response[offset + 2]);

                if (dtc == 0xA9060C || dtc == 0xA9060E)
                {
                    Console.WriteLine($"[Camera] Calibration blocking DTC detected: 0x{dtc:X6}");
                    return false;
                }
            }

            Console.WriteLine("[Camera] No blocking DTCs found - OK to calibrate");
            return true;
        }

        // Close Target Calibration ($FE01, 0x00) - Security Access 필요
        public void CameraCloseTargetCalibration()
        {
            SendUDSCommand(new byte[] { 0x31, 0x01, 0xFE, 0x01, 0x00 });  // Start
            SendUDSCommand(new byte[] { 0x31, 0x03, 0xFE, 0x01, 0x00 });  // Result
            SendUDSCommand(new byte[] { 0x31, 0x02, 0xFE, 0x01, 0x00 });  // Stop
            Console.WriteLine("[Camera] Close Target Calibration completed");
        }

        // Far Target Calibration ($FE01, 0x01) - Security Access 필요
        public void CameraFarTargetCalibration()
        {
            SendUDSCommand(new byte[] { 0x31, 0x01, 0xFE, 0x01, 0x01 });  // Start
            SendUDSCommand(new byte[] { 0x31, 0x03, 0xFE, 0x01, 0x01 });  // Result
            SendUDSCommand(new byte[] { 0x31, 0x02, 0xFE, 0x01, 0x01 });  // Stop
            Console.WriteLine("[Camera] Far Target Calibration completed");
        }

        // Dynamic Calibration Routine ($FE02) - Security Access 필요
        public void CameraDynamicCalibrationRoutine()
        {
            SendUDSCommand(new byte[] { 0x31, 0x01, 0xFE, 0x02 });  // Start
            SendUDSCommand(new byte[] { 0x31, 0x03, 0xFE, 0x02 });  // Result
            SendUDSCommand(new byte[] { 0x31, 0x02, 0xFE, 0x02 });  // Stop
            Console.WriteLine("[Camera] Dynamic Calibration Routine completed");
        }

        // Read Calibration Result ($FD11) - Security Access 불필요
        public byte[] CameraReadCalibrationResult()
        {
            byte[] response = SendUDSCommand(new byte[] { 0x22, 0xFD, 0x11 });
            Console.WriteLine($"[Camera] Calibration Result ($FD11): {BitConverter.ToString(response)}");
            return response;
        }

        // Vehicle Data Write ($FD10) - Security Access 불필요
        // 14 bytes: LeftWheel(2) + RightWheel(2) + DistToHead(2) + LateralToCenter(2) + ChasisNumber(4) + CountryCode(1) + Status(1)
        public void CameraVehicleDataWrite(byte[] vehicleData)
        {
            if (vehicleData.Length != 14)
                throw new ArgumentException("Vehicle data must be 14 bytes (DID $FD10 spec)");

            byte[] header = new byte[] { 0x2E, 0xFD, 0x10 };
            byte[] writeCmd = header.Concat(vehicleData).ToArray();
            SendUDSCommand(writeCmd);
            Console.WriteLine("[Camera] Vehicle Data Write completed");
        }

        // Camera Static Calibration (EOL) 전체 시퀀스
        public void CameraStaticCalibrationEOL(byte[] vehicleData)
        {
            if (vehicleData == null || vehicleData.Length != 14)
                throw new ArgumentException("Vehicle data must be 14 bytes");

            // Step 1: Extended Session + Vehicle Data Write + Clear DTCs (Security 불필요)
            _logger?.LogStep("Step 1: Extended Session + Vehicle Data Write + Clear DTC");
            EnterExtendedSession();
            CameraVehicleDataWrite(vehicleData);
            ClearDTC();

            // Step 2: Reset ECU
            _logger?.LogStep("Step 2: ECU Reset");
            EcuReset();
            Thread.Sleep(2000);

            // Step 3: Check DTCs
            _logger?.LogStep("Step 3: DTC Check (Blocking DTC)");
            EnterExtendedSession();
            if (!CameraCheckCalibrationDTC())
                throw new Exception("[Camera] Calibration blocking DTC detected (0xA9060C or 0xA9060E)");

            // Step 4: Security Access + SPTAC CLOSE (Security 필요)
            _logger?.LogStep("Step 4: Security Access + Close Target Calibration");
            CameraSecurityAccess();
            CameraCloseTargetCalibration();

            // Step 5: SPTAC FAR
            _logger?.LogStep("Step 5: Far Target Calibration");
            CameraFarTargetCalibration();

            // Step 6: Reset ECU
            _logger?.LogStep("Step 6: ECU Reset");
            EcuReset();
            Thread.Sleep(2000);

            // Step 7: Verify calibration (DID $FD11)
            _logger?.LogStep("Step 7: Read Calibration Result ($FD11)");
            EnterExtendedSession();
            byte[] calibResult = CameraReadCalibrationResult();
            if (calibResult.Length >= 11 && calibResult[10] != 0xAA)
                throw new Exception($"[Camera] SPTAC calibration verification failed: Status=0x{calibResult[10]:X2}");

            // Step 8: Clear and check DTCs
            _logger?.LogStep("Step 8: Clear DTC + Final DTC Check");
            ClearDTC();
            Thread.Sleep(10000);
            CheckDTC();

            Console.WriteLine("=== [Camera] Static Calibration (EOL) completed successfully ===");
        }

        // Camera Dynamic Calibration 전체 시퀀스
        public void CameraDynamicCalibration(byte[] vehicleData)
        {
            if (vehicleData == null || vehicleData.Length != 14)
                throw new ArgumentException("Vehicle data must be 14 bytes");

            // Step 1: Extended Session + Vehicle Data Write + Clear DTCs
            _logger?.LogStep("Step 1: Extended Session + Vehicle Data Write + Clear DTC");
            EnterExtendedSession();
            CameraVehicleDataWrite(vehicleData);
            ClearDTC();

            // Step 2: Reset ECU
            _logger?.LogStep("Step 2: ECU Reset");
            EcuReset();
            Thread.Sleep(2000);

            // Step 3: Check DTCs
            _logger?.LogStep("Step 3: DTC Check (Blocking DTC)");
            EnterExtendedSession();
            if (!CameraCheckCalibrationDTC())
                throw new Exception("[Camera] Calibration blocking DTC detected (0xA9060C or 0xA9060E)");

            // Step 4: Security Access + Dynamic Calibration
            _logger?.LogStep("Step 4: Security Access + Dynamic Calibration Routine");
            CameraSecurityAccess();
            CameraDynamicCalibrationRoutine();

            // Step 5: Reset ECU
            _logger?.LogStep("Step 5: ECU Reset");
            EcuReset();
            Thread.Sleep(2000);

            // Step 6: Verify DC calibration
            _logger?.LogStep("Step 6: Read Calibration Result ($FD11)");
            EnterExtendedSession();
            byte[] calibResult = CameraReadCalibrationResult();
            if (calibResult.Length >= 11 && calibResult[10] != 0xAA)
                throw new Exception($"[Camera] DC calibration verification failed: Status=0x{calibResult[10]:X2}");

            // Step 7: Clear and check DTCs
            _logger?.LogStep("Step 7: Clear DTC + Final DTC Check");
            ClearDTC();
            Thread.Sleep(10000);
            CheckDTC();

            Console.WriteLine("=== [Camera] Dynamic Calibration completed successfully ===");
        }

        #endregion

        #region Radar ECU Methods (Y526316 문서 기준)
        // ============================================================================
        // Radar ECU: FLR26
        // - CAN ID: 0x18DA2AF0 (Tester) / 0x18DAF02A (ECU) - 29-bit extended (J1939)
        // - Security Access: Routine Control ($31)에는 불필요, DID Write ($2E)에만 필요
        // - Security 알고리즘: (seed * 9835) + 6558 (Y526316 문서에 명시)
        // ============================================================================

        // Radar EOL Alignment ($FFA0) - Security Access 불필요
        // Y526316 문서 Page 6: "EOL Alignment Routine"
        public bool RadarEOLAlignment()
        {
            SendUDSCommand(new byte[] { 0x31, 0x01, 0xFF, 0xA0 });  // Start
            Console.WriteLine("[Radar] EOL Alignment started");

            byte[] result = SendUDSCommand(new byte[] { 0x31, 0x03, 0xFF, 0xA0 });  // Result
            // 응답: [SubFunc(03)] + [RID(FFA0)] + [Status]
            bool success = result.Length >= 4 && result[3] == 0x00;

            if (!success && result.Length >= 5)
            {
                Console.WriteLine($"[Radar] EOL Alignment failed: ErrorCode=0x{result[4]:X2}");
            }

            SendUDSCommand(new byte[] { 0x31, 0x02, 0xFF, 0xA0 });  // Stop
            Console.WriteLine($"[Radar] EOL Alignment completed: {(success ? "SUCCESS" : "FAILED")}");

            return success;
        }

        // Radar Alignment Data 읽기 ($FEA7) - 48 bytes
        public byte[] RadarReadAlignmentData()
        {
            byte[] response = SendUDSCommand(new byte[] { 0x22, 0xFE, 0xA7 });
            Console.WriteLine($"[Radar] Alignment Data ($FEA7): {BitConverter.ToString(response)}");
            return response;
        }

        // Radar 전체 EOL 테스트 시퀀스
        public void RadarEOLTest()
        {
            Console.WriteLine("=== [Radar] Starting EOL Test ===");

            // Step 1: Extended Session
            _logger?.LogStep("Step 1: Extended Session");
            EnterExtendedSession();
            Console.WriteLine("[Radar] Step 1: Extended Session - OK");

            // Step 2: DTC Check
            _logger?.LogStep("Step 2: DTC Check");
            byte[] dtcResult = CheckDTC();
            Console.WriteLine($"[Radar] Step 2: DTC Check - {dtcResult.Length} bytes");

            // Step 3: EOL Alignment (Security Access 불필요!)
            _logger?.LogStep("Step 3: EOL Alignment ($FFA0)");
            bool alignResult = RadarEOLAlignment();
            Console.WriteLine($"[Radar] Step 3: EOL Alignment - {(alignResult ? "PASS" : "FAIL")}");

            // Step 4: ECU Reset
            _logger?.LogStep("Step 4: ECU Reset");
            EcuReset();
            Thread.Sleep(2000);
            Console.WriteLine("[Radar] Step 4: ECU Reset - OK");

            // Step 5: Extended Session (재진입)
            _logger?.LogStep("Step 5: Extended Session (Re-enter)");
            EnterExtendedSession();

            // Step 6: Read Alignment Data
            _logger?.LogStep("Step 6: Read Alignment Data ($FEA7)");
            byte[] alignData = RadarReadAlignmentData();
            Console.WriteLine($"[Radar] Step 5: Read Alignment Data - {alignData.Length} bytes");

            // Step 7: Clear DTC
            _logger?.LogStep("Step 7: Clear DTC");
            ClearDTC();
            Console.WriteLine("[Radar] Step 6: Clear DTC - OK");

            // Step 8: Final DTC Check
            _logger?.LogStep("Step 8: Final DTC Check");
            dtcResult = CheckDTC();
            Console.WriteLine($"[Radar] Step 7: Final DTC Check - {dtcResult.Length} bytes");

            Console.WriteLine("=== [Radar] EOL Test Completed ===");
        }

        #endregion
    }
}

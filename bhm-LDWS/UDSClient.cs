using KI_RnB;

using System;
using System.Collections.Concurrent;
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

        // COM 스레드 안전을 위한 전용 STA 스레드
        private Thread _comThread;
        private BlockingCollection<Action> _comQueue = new BlockingCollection<Action>();

        // CSnet1과 동일: 메시지 버퍼 20000개
        private icsSpyMessage[] stMessages = new icsSpyMessage[20000];

        // Logger for TX/RX logging
        private Logger _logger;
        public Logger Logger
        {
            get => _logger;
            set => _logger = value;
        }

        // UI 로그 콜백 (MainWindow에서 설정)
        public Action<string> OnLog { get; set; }

        // Camera CAN IDs (29-bit extended, J1939) - Security Access 문서 기준
        // "Request on CAN Id 0x18 DA E8 xx" → ECU=0xE8, Tester=0xF0
        private const uint CameraTesterID = 0x18DA2AF1;  // 0x18DAE8F0;
        private const uint CameraEcuID = 0x18DAF12A;  // 0x18DAF0E8;

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
            // COM 전용 STA 스레드 시작
            _comThread = new Thread(ComThreadLoop);
            _comThread.SetApartmentState(ApartmentState.STA);
            _comThread.IsBackground = true;
            _comThread.Start();

            // COM 스레드에서 장치 열기
            RunOnComThread(() => OpenDevice());

            _heartbeatTimer = new Timer(_ => SendHeartbeat(), null, Timeout.Infinite, Timeout.Infinite);
        }

        // COM 스레드 루프
        private void ComThreadLoop()
        {
            foreach (var action in _comQueue.GetConsumingEnumerable())
            {
                try { action(); }
                catch { /* 에러 무시 */ }
            }
        }

        // COM 스레드에서 작업 실행 (동기)
        private void RunOnComThread(Action action)
        {
            if (Thread.CurrentThread == _comThread)
            {
                action();
                return;
            }

            var tcs = new TaskCompletionSource<bool>();
            _comQueue.Add(() =>
            {
                try
                {
                    action();
                    tcs.SetResult(true);
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            });

            try
            {
                tcs.Task.Wait();
            }
            catch (AggregateException ae)
            {
                throw ae.InnerException ?? ae;
            }
        }

        // COM 스레드에서 작업 실행 (반환값 있음)
        private T RunOnComThread<T>(Func<T> func)
        {
            if (Thread.CurrentThread == _comThread)
            {
                return func();
            }

            var tcs = new TaskCompletionSource<T>();
            _comQueue.Add(() =>
            {
                try
                {
                    tcs.SetResult(func());
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            });

            try
            {
                return tcs.Task.Result;
            }
            catch (AggregateException ae)
            {
                // 원본 예외를 던짐
                throw ae.InnerException ?? ae;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        public virtual void Dispose(bool disposing)
        {
            if (_disposed) return;
            _disposed = true;

            if (disposing)
            {
                // 1. 타이머 중지
                if (_heartbeatTimer != null)
                {
                    _heartbeatTimer.Dispose();
                    _heartbeatTimer = null;
                }

                // 2. COM 스레드에서 장치 닫기
                if (hObject != IntPtr.Zero && _comQueue != null && !_comQueue.IsAddingCompleted)
                {
                    try
                    {
                        RunOnComThread(() =>
                        {
                            if (hObject != IntPtr.Zero)
                            {
                                int errors = 0;
                                icsNeoDll.icsneoClosePort(hObject, ref errors);
                                icsNeoDll.icsneoFreeObject(hObject);
                                hObject = IntPtr.Zero;
                            }
                        });
                    }
                    catch { }
                }

                // 3. COM 스레드 큐 종료
                if (_comQueue != null && !_comQueue.IsAddingCompleted)
                {
                    _comQueue.CompleteAdding();
                }

                // 4. COM 스레드가 종료될 때까지 대기
                if (_comThread != null && _comThread.IsAlive)
                {
                    _comThread.Join(1000);  // 최대 1초 대기
                }

                // 5. 큐 해제
                _comQueue?.Dispose();
                _comQueue = null;
            }
            // finalizer (disposing=false)에서는 관리 객체 접근 금지
        }

        ~UDSClient()
        {
            Dispose(false);
        }

        #endregion

        #region Device & Communication Core

        // CSnet1과 완전히 동일한 OpenDevice
        public void OpenDevice()
        {
            // 기존 핸들이 있으면 먼저 정리
            if (hObject != IntPtr.Zero)
            {
                try
                {
                    int errors = 0;
                    icsNeoDll.icsneoClosePort(hObject, ref errors);
                    icsNeoDll.icsneoFreeObject(hObject);
                }
                catch { }
                hObject = IntPtr.Zero;
            }

            NeoDeviceEx[] ndNeoToOpenEx = new NeoDeviceEx[16];
            NeoDevice ndNeoToOpen;
            OptionsNeoEx neoDeviceOption = new OptionsNeoEx();
            int iNumberOfDevices = 15;

            // CSnet1: byte[255] 배열에 0~254 값 채우기
            byte[] bNetwork = new byte[255];
            for (int i = 0; i < 255; i++)
                bNetwork[i] = (byte)i;

            // CSnet1: icsneoFindDevices 사용
            int iResult = icsNeoDll.icsneoFindDevices(ref ndNeoToOpenEx[0], ref iNumberOfDevices, 0, 0, ref neoDeviceOption, 0);
            if (iResult == 0 || iNumberOfDevices < 1)
            {
                throw new Exception("No neoVI device found");
            }

            ndNeoToOpen = ndNeoToOpenEx[0].neoDevice;

            // CSnet1: configRead=1, syncToPC=0
            iResult = icsNeoDll.icsneoOpenNeoDevice(ref ndNeoToOpen, ref hObject, ref bNetwork[0], 1, 0);
            if (iResult == 0)
            {
                throw new Exception("Failed to open neoVI device");
            }
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

        // CSnet1과 완전히 동일한 TX (COM 스레드에서 실행)
        public byte[] SendUDSCommand(byte[] command, bool expectResponse = true)
        {
            return RunOnComThread(() => SendUDSCommandInternal(command, expectResponse));
        }

        private byte[] SendUDSCommandInternal(byte[] command, bool expectResponse)
        {
            uint testerID = GetTesterID();
            bool isExtended = true;  // 29-bit extended ID

            // CSnet1과 동일: 새 메시지 구조체 생성
            icsSpyMessage stMessagesTx = new icsSpyMessage();

            // CSnet1과 동일: Extended ID면 XTD_FRAME만, 아니면 0
            if (isExtended)
                stMessagesTx.StatusBitField = (int)eDATA_STATUS_BITFIELD_1.SPY_STATUS_XTD_FRAME;
            else
                stMessagesTx.StatusBitField = 0;

            stMessagesTx.ArbIDOrHeader = (int)testerID;

            if (command.Length <= 7)
            {
                // Single Frame
                byte[] frame = new byte[MaxFrameSize];
                frame[0] = (byte)command.Length;
                Array.Copy(command, 0, frame, 1, command.Length);

                // CSnet1과 동일: NumberBytesData와 Data1~8 직접 설정
                stMessagesTx.NumberBytesData = 8;
                stMessagesTx.Data1 = frame[0];
                stMessagesTx.Data2 = frame[1];
                stMessagesTx.Data3 = frame[2];
                stMessagesTx.Data4 = frame[3];
                stMessagesTx.Data5 = frame[4];
                stMessagesTx.Data6 = frame[5];
                stMessagesTx.Data7 = frame[6];
                stMessagesTx.Data8 = frame[7];

                // CSnet1과 동일: icsneoTxMessages(handle, ref msg, networkID, 1)
                icsNeoDll.icsneoTxMessages(hObject, ref stMessagesTx, (int)eNETWORK_ID.NETID_HSCAN, 1);
                _logger?.LogTx(testerID, frame);
                OnLog?.Invoke($"TX >> 0x{testerID:X8} | {BitConverter.ToString(frame)}");
            }
            else
            {
                // First Frame
                byte[] ff = new byte[MaxFrameSize];
                ff[0] = (byte)(0x10 | ((command.Length >> 8) & 0x0F));
                ff[1] = (byte)(command.Length & 0xFF);
                Array.Copy(command, 0, ff, 2, 6);

                stMessagesTx.NumberBytesData = 8;
                stMessagesTx.Data1 = ff[0];
                stMessagesTx.Data2 = ff[1];
                stMessagesTx.Data3 = ff[2];
                stMessagesTx.Data4 = ff[3];
                stMessagesTx.Data5 = ff[4];
                stMessagesTx.Data6 = ff[5];
                stMessagesTx.Data7 = ff[6];
                stMessagesTx.Data8 = ff[7];

                icsNeoDll.icsneoTxMessages(hObject, ref stMessagesTx, (int)eNETWORK_ID.NETID_HSCAN, 1);
                _logger?.LogTx(testerID, ff);
                OnLog?.Invoke($"TX >> 0x{testerID:X8} | {BitConverter.ToString(ff)}");

                // Flow Control 대기
                byte[] fc = ReceiveSingleFrame(2000);
                if (fc == null || fc.Length == 0 || fc[0] != 0x30)
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

                    stMessagesTx.Data1 = cf[0];
                    stMessagesTx.Data2 = cf[1];
                    stMessagesTx.Data3 = cf[2];
                    stMessagesTx.Data4 = cf[3];
                    stMessagesTx.Data5 = cf[4];
                    stMessagesTx.Data6 = cf[5];
                    stMessagesTx.Data7 = cf[6];
                    stMessagesTx.Data8 = cf[7];

                    icsNeoDll.icsneoTxMessages(hObject, ref stMessagesTx, (int)eNETWORK_ID.NETID_HSCAN, 1);
                    _logger?.LogTx(testerID, cf);
                    OnLog?.Invoke($"TX >> 0x{testerID:X8} | {BitConverter.ToString(cf)}");
                    offset += len;
                    if (sequenceNumber > 0x2F) sequenceNumber = 0x20;
                    Thread.Sleep(50);
                }
            }

            if (!expectResponse) return null;

            return ReceiveUDSResponse(command[0]);
        }

        // CSnet1과 완전히 동일한 RX
        public byte[] ReceiveSingleFrame(int timeoutMs = 2000)
        {
            uint expectedEcuID = GetEcuID();
            DateTime startTime = DateTime.Now;

            while ((DateTime.Now - startTime).TotalMilliseconds < timeoutMs)
            {
                // CSnet1: icsneoWaitForRxMessagesWithTimeOut 사용
                int waitResult = icsNeoDll.icsneoWaitForRxMessagesWithTimeOut(hObject, 100);
                if (waitResult == 0) continue;

                // CSnet1과 동일: 배열 버퍼 사용
                int lNumberOfMessages = 0;
                int lNumberOfErrors = 0;

                // CSnet1과 동일: icsneoGetMessages(handle, ref buffer[0], ref count, ref errors)
                int lResult = icsNeoDll.icsneoGetMessages(hObject, ref stMessages[0], ref lNumberOfMessages, ref lNumberOfErrors);
                if (lResult == 0 || lNumberOfMessages == 0)
                    continue;

                // CSnet1과 동일: 메시지 순회
                for (int i = 0; i < lNumberOfMessages; i++)
                {
                    uint receivedID = (uint)stMessages[i].ArbIDOrHeader;

                    // TX 메시지 무시 (StatusBitField에 TX_MSG 플래그 확인)
                    if ((stMessages[i].StatusBitField & (int)eDATA_STATUS_BITFIELD_1.SPY_STATUS_TX_MSG) != 0)
                        continue;

                    // ECU ID 필터링
                    if (receivedID != expectedEcuID)
                        continue;

                    // CSnet1과 동일: Data1~8에서 데이터 추출
                    byte[] rxData = new byte[stMessages[i].NumberBytesData];
                    if (rxData.Length >= 1) rxData[0] = stMessages[i].Data1;
                    if (rxData.Length >= 2) rxData[1] = stMessages[i].Data2;
                    if (rxData.Length >= 3) rxData[2] = stMessages[i].Data3;
                    if (rxData.Length >= 4) rxData[3] = stMessages[i].Data4;
                    if (rxData.Length >= 5) rxData[4] = stMessages[i].Data5;
                    if (rxData.Length >= 6) rxData[5] = stMessages[i].Data6;
                    if (rxData.Length >= 7) rxData[6] = stMessages[i].Data7;
                    if (rxData.Length >= 8) rxData[7] = stMessages[i].Data8;

                    _logger?.LogRx(receivedID, rxData);
                    OnLog?.Invoke($"RX << 0x{receivedID:X8} | {BitConverter.ToString(rxData)}");
                    return rxData;
                }
            }

            return null;
        }

        // UDS 응답 수신 (Multi-frame 지원) - PCI/SID 제외, 순수 데이터만 반환
        public byte[] ReceiveUDSResponse(byte originalSid)
        {
            byte[] first = ReceiveSingleFrame(5000);
            if (first == null || first.Length == 0)
                throw new Exception("No response received (timeout)");

            if ((first[0] & 0xF0) == 0x10)  // First Frame (Multi-frame)
            {
                int respLen = ((first[0] & 0x0F) << 8) | first[1];
                List<byte> fullResp = new List<byte>(first.Skip(2).Take(6));  // FF에서 데이터 6바이트

                // Flow Control 전송 (Raw 프레임으로 직접 전송)
                byte[] fc = { 0x30, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
                SendRawFrame(fc);

                // 멀티프레임 일괄 수신 (버퍼 손실 방지)
                uint expectedEcuID = GetEcuID();
                DateTime startTime = DateTime.Now;
                int timeoutMs = 5000;

                while (fullResp.Count < respLen && (DateTime.Now - startTime).TotalMilliseconds < timeoutMs)
                {
                    int waitResult = icsNeoDll.icsneoWaitForRxMessagesWithTimeOut(hObject, 100);
                    if (waitResult == 0) continue;

                    int lNumberOfMessages = 0;
                    int lNumberOfErrors = 0;
                    icsNeoDll.icsneoGetMessages(hObject, ref stMessages[0], ref lNumberOfMessages, ref lNumberOfErrors);

                    // 버퍼의 모든 CF를 한 번에 처리
                    for (int i = 0; i < lNumberOfMessages && fullResp.Count < respLen; i++)
                    {
                        if ((stMessages[i].StatusBitField & (int)eDATA_STATUS_BITFIELD_1.SPY_STATUS_TX_MSG) != 0)
                            continue;
                        if ((uint)stMessages[i].ArbIDOrHeader != expectedEcuID)
                            continue;

                        byte pci = stMessages[i].Data1;
                        if ((pci & 0xF0) == 0x20)  // Consecutive Frame
                        {
                            int remaining = respLen - fullResp.Count;
                            int bytesToTake = Math.Min(7, remaining);
                            byte[] cfData = { stMessages[i].Data2, stMessages[i].Data3, stMessages[i].Data4,
                                              stMessages[i].Data5, stMessages[i].Data6, stMessages[i].Data7, stMessages[i].Data8 };
                            fullResp.AddRange(cfData.Take(bytesToTake));
                        }
                    }
                }

                if (fullResp.Count < respLen)
                    throw new Exception($"Multi-frame incomplete: {fullResp.Count}/{respLen} bytes");

                byte[] response = fullResp.ToArray();
                byte responseSid = response[0];

                if (responseSid == 0x7F)
                {
                    OnLog?.Invoke($"[NRC] Negative Response: SID=0x{response[1]:X2}, NRC=0x{response[2]:X2} ({GetNrcDescription(response[2])})");
                    return null;
                }

                if (responseSid != (originalSid + 0x40))
                    throw new Exception($"Invalid Positive Response SID (expected {originalSid + 0x40:X2})");

                return response.Skip(1).ToArray();
            }
            else  // Single Frame
            {
                int dataLen = first[0] & 0x0F;
                byte responseSid = first[1];

                if (responseSid == 0x7F)
                {
                    OnLog?.Invoke($"[NRC] Negative Response: SID=0x{first[2]:X2}, NRC=0x{first[3]:X2} ({GetNrcDescription(first[3])})");
                    return null;
                }

                if (responseSid != (originalSid + 0x40))
                    throw new Exception($"Invalid Positive Response SID (expected {originalSid + 0x40:X2}, got {responseSid:X2})");

                return first.Skip(2).Take(dataLen - 1).ToArray();
            }
        }

        // NRC 코드 설명
        private string GetNrcDescription(byte nrc)
        {
            switch (nrc)
            {
                case 0x10: return "generalReject";
                case 0x11: return "serviceNotSupported";
                case 0x12: return "subFunctionNotSupported";
                case 0x13: return "incorrectMessageLengthOrInvalidFormat";
                case 0x14: return "responseTooLong";
                case 0x21: return "busyRepeatRequest";
                case 0x22: return "conditionsNotCorrect";
                case 0x24: return "requestSequenceError";
                case 0x25: return "noResponseFromSubnetComponent";
                case 0x26: return "failurePreventsExecutionOfRequestedAction";
                case 0x31: return "requestOutOfRange";
                case 0x33: return "securityAccessDenied";
                case 0x35: return "invalidKey";
                case 0x36: return "exceededNumberOfAttempts";
                case 0x37: return "requiredTimeDelayNotExpired";
                case 0x70: return "uploadDownloadNotAccepted";
                case 0x71: return "transferDataSuspended";
                case 0x72: return "generalProgrammingFailure";
                case 0x73: return "wrongBlockSequenceCounter";
                case 0x78: return "requestCorrectlyReceivedResponsePending";
                case 0x7E: return "subFunctionNotSupportedInActiveSession";
                case 0x7F: return "serviceNotSupportedInActiveSession";
                default: return $"Unknown (0x{nrc:X2})";
            }
        }

        // Raw CAN 프레임 전송 (Flow Control 등 UDS 프레이밍 없이 직접 전송)
        private void SendRawFrame(byte[] data)
        {
            uint testerID = GetTesterID();

            icsSpyMessage stMessagesTx = new icsSpyMessage();
            stMessagesTx.StatusBitField = (int)eDATA_STATUS_BITFIELD_1.SPY_STATUS_XTD_FRAME;
            stMessagesTx.ArbIDOrHeader = (int)testerID;
            stMessagesTx.NumberBytesData = 8;
            stMessagesTx.Data1 = data.Length > 0 ? data[0] : (byte)0;
            stMessagesTx.Data2 = data.Length > 1 ? data[1] : (byte)0;
            stMessagesTx.Data3 = data.Length > 2 ? data[2] : (byte)0;
            stMessagesTx.Data4 = data.Length > 3 ? data[3] : (byte)0;
            stMessagesTx.Data5 = data.Length > 4 ? data[4] : (byte)0;
            stMessagesTx.Data6 = data.Length > 5 ? data[5] : (byte)0;
            stMessagesTx.Data7 = data.Length > 6 ? data[6] : (byte)0;
            stMessagesTx.Data8 = data.Length > 7 ? data[7] : (byte)0;

            icsNeoDll.icsneoTxMessages(hObject, ref stMessagesTx, (int)eNETWORK_ID.NETID_HSCAN, 1);
            _logger?.LogTx(testerID, data);
            OnLog?.Invoke($"TX >> 0x{testerID:X8} | {BitConverter.ToString(data)}");
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
        }

        // DTC Check (0x19 0x02)
        public byte[] CheckDTC()
        {
            byte[] dtcCmd = { 0x19, 0x02, 0xFF };
            return SendUDSCommand(dtcCmd);
        }

        // Clear DTC (0x14)
        public void ClearDTC()
        {
            SendUDSCommand(new byte[] { 0x14, 0xFF, 0xFF, 0xFF });
        }

        // ECU Reset (0x11 0x01)
        public void EcuReset()
        {
            SendUDSCommand(new byte[] { 0x11, 0x01 });
        }

        // Heartbeat (0x3E 0x80) - Tester Present
        public void SendHeartbeat()
        {
            byte[] hb = { 0x3E, 0x80 };  // SuppressPosRspMsgIndicationBit
            SendUDSCommand(hb, false);
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
            uint swapped = (seed >> 16) | (seed << 16);
            uint key = swapped % 0xC503U;

            byte[] keyBytes = BitConverter.GetBytes(key);
            byte[] keyReq = new byte[] { 0x27, 0x02 }.Concat(keyBytes).ToArray();
            byte[] keyResp = SendUDSCommand(keyReq);

            if (keyResp.Length < 1 || keyResp[0] != 0x02)
                throw new Exception($"Security Key failed: {BitConverter.ToString(keyResp)}");
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
                    return false;
            }

            return true;
        }

        // Close Target Calibration ($FE01, 0x00) - Security Access 필요
        public void CameraCloseTargetCalibration()
        {
            SendUDSCommand(new byte[] { 0x31, 0x01, 0xFE, 0x01, 0x00 });  // Start
            SendUDSCommand(new byte[] { 0x31, 0x03, 0xFE, 0x01, 0x00 });  // Result
            SendUDSCommand(new byte[] { 0x31, 0x02, 0xFE, 0x01, 0x00 });  // Stop
        }

        // Far Target Calibration ($FE01, 0x01) - Security Access 필요
        public void CameraFarTargetCalibration()
        {
            SendUDSCommand(new byte[] { 0x31, 0x01, 0xFE, 0x01, 0x01 });  // Start
            SendUDSCommand(new byte[] { 0x31, 0x03, 0xFE, 0x01, 0x01 });  // Result
            SendUDSCommand(new byte[] { 0x31, 0x02, 0xFE, 0x01, 0x01 });  // Stop
        }

        // Dynamic Calibration Routine ($FE02) - Security Access 필요
        public void CameraDynamicCalibrationRoutine()
        {
            SendUDSCommand(new byte[] { 0x31, 0x01, 0xFE, 0x02 });  // Start
            SendUDSCommand(new byte[] { 0x31, 0x03, 0xFE, 0x02 });  // Result
            SendUDSCommand(new byte[] { 0x31, 0x02, 0xFE, 0x02 });  // Stop
        }

        // Read Calibration Result ($FD11) - Security Access 불필요
        public byte[] CameraReadCalibrationResult()
        {
            return SendUDSCommand(new byte[] { 0x22, 0xFD, 0x11 });
        }

        // Vehicle Data Write ($FD10) - Security Access 불필요
        public void CameraVehicleDataWrite(byte[] vehicleData)
        {
            if (vehicleData.Length != 14)
                throw new ArgumentException("Vehicle data must be 14 bytes (DID $FD10 spec)");

            byte[] header = new byte[] { 0x2E, 0xFD, 0x10 };
            byte[] writeCmd = header.Concat(vehicleData).ToArray();
            SendUDSCommand(writeCmd);
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
        public bool RadarEOLAlignment()
        {
            SendUDSCommand(new byte[] { 0x31, 0x01, 0xFF, 0xA0 });  // Start
            byte[] result = SendUDSCommand(new byte[] { 0x31, 0x03, 0xFF, 0xA0 });  // Result
            bool success = result.Length >= 4 && result[3] == 0x00;
            SendUDSCommand(new byte[] { 0x31, 0x02, 0xFF, 0xA0 });  // Stop
            return success;
        }

        // Radar Alignment Data 읽기 ($FEA7)
        public byte[] RadarReadAlignmentData()
        {
            return SendUDSCommand(new byte[] { 0x22, 0xFE, 0xA7 });
        }

        // Radar 전체 EOL 테스트 시퀀스
        public void RadarEOLTest()
        {
            // Step 1: Extended Session
            _logger?.LogStep("Step 1: Extended Session");
            EnterExtendedSession();

            // Step 2: DTC Check
            _logger?.LogStep("Step 2: DTC Check");
            CheckDTC();

            // Step 3: EOL Alignment
            _logger?.LogStep("Step 3: EOL Alignment ($FFA0)");
            RadarEOLAlignment();

            // Step 4: ECU Reset
            _logger?.LogStep("Step 4: ECU Reset");
            EcuReset();
            Thread.Sleep(2000);

            // Step 5: Extended Session (재진입)
            _logger?.LogStep("Step 5: Extended Session (Re-enter)");
            EnterExtendedSession();

            // Step 6: Read Alignment Data
            _logger?.LogStep("Step 6: Read Alignment Data ($FEA7)");
            RadarReadAlignmentData();

            // Step 7: Clear DTC
            _logger?.LogStep("Step 7: Clear DTC");
            ClearDTC();

            // Step 8: Final DTC Check
            _logger?.LogStep("Step 8: Final DTC Check");
            CheckDTC();
        }

        #endregion
    }
}

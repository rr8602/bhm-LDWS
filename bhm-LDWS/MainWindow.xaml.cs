using bhm_LDWS;

using System;
using System.Threading.Tasks;
using System.Windows;

namespace KI_RnB
{
    public partial class MainWindow : Window
    {
        private UDSClient _client;

        public MainWindow()
        {
            InitializeComponent();

            try
            {
                _client = new UDSClient();
                AppendLog("UDS Client initialized successfully.");
            }
            catch (Exception ex)
            {
                AppendLog($"Initialization failed: {ex.Message}");
                MessageBox.Show($"Device open failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void AppendLog(string message)
        {
            Dispatcher.Invoke(() =>
            {
                txtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}\r\n");
                txtLog.ScrollToEnd();
            });
        }

        #region Camera - Static Calibration (EOL) Buttons

        private async void btnExtendedSession_Click(object sender, RoutedEventArgs e)
        {
            btnExtendedSession.IsEnabled = false;
            AppendLog("[Camera] Starting Extended Session...");

            try
            {
                _client.CurrentEcuType = EcuType.Camera;
                await Task.Run(() => _client.EnterExtendedSession());
                AppendLog("[Camera] Extended Session entered.");
            }
            catch (Exception ex)
            {
                AppendLog($"[Camera] Extended Session error: {ex.Message}");
            }
            finally
            {
                btnExtendedSession.IsEnabled = true;
            }
        }

        private async void btnDtcCheck_Click(object sender, RoutedEventArgs e)
        {
            btnDtcCheck.IsEnabled = false;
            AppendLog("[Camera] Checking DTC...");

            try
            {
                _client.CurrentEcuType = EcuType.Camera;
                await Task.Run(() => _client.CheckDTC());
                AppendLog("[Camera] DTC Check completed.");
            }
            catch (Exception ex)
            {
                AppendLog($"[Camera] DTC Check error: {ex.Message}");
            }
            finally
            {
                btnDtcCheck.IsEnabled = true;
            }
        }

        private async void btnSecurityAccess_Click(object sender, RoutedEventArgs e)
        {
            btnSecurityAccess.IsEnabled = false;
            AppendLog("[Camera] Performing Security Access...");

            try
            {
                _client.CurrentEcuType = EcuType.Camera;
                await Task.Run(() => _client.CameraSecurityAccess());
                AppendLog("[Camera] Security Access successful.");
            }
            catch (Exception ex)
            {
                AppendLog($"[Camera] Security Access error: {ex.Message}");
            }
            finally
            {
                btnSecurityAccess.IsEnabled = true;
            }
        }

        private async void btnCloseTarget_Click(object sender, RoutedEventArgs e)
        {
            btnCloseTarget.IsEnabled = false;
            AppendLog("[Camera] Starting Close Target Calibration...");

            try
            {
                _client.CurrentEcuType = EcuType.Camera;
                await Task.Run(() => _client.CameraCloseTargetCalibration());
                AppendLog("[Camera] Close Target Calibration completed.");
            }
            catch (Exception ex)
            {
                AppendLog($"[Camera] Close Target Calibration error: {ex.Message}");
            }
            finally
            {
                btnCloseTarget.IsEnabled = true;
            }
        }

        private async void btnHeartbeat_Click(object sender, RoutedEventArgs e)
        {
            btnHeartbeat.IsEnabled = false;
            AppendLog("[Camera] Sending Heartbeat...");

            try
            {
                _client.CurrentEcuType = EcuType.Camera;
                await Task.Run(() => _client.SendHeartbeat());
                AppendLog("[Camera] Heartbeat sent.");
            }
            catch (Exception ex)
            {
                AppendLog($"[Camera] Heartbeat error: {ex.Message}");
            }
            finally
            {
                btnHeartbeat.IsEnabled = true;
            }
        }

        private async void btnFarTarget_Click(object sender, RoutedEventArgs e)
        {
            btnFarTarget.IsEnabled = false;
            AppendLog("[Camera] Starting Far Target Calibration...");

            try
            {
                _client.CurrentEcuType = EcuType.Camera;
                await Task.Run(() => _client.CameraFarTargetCalibration());
                AppendLog("[Camera] Far Target Calibration completed.");
            }
            catch (Exception ex)
            {
                AppendLog($"[Camera] Far Target Calibration error: {ex.Message}");
            }
            finally
            {
                btnFarTarget.IsEnabled = true;
            }
        }

        private async void btnStaticEcuReset_Click(object sender, RoutedEventArgs e)
        {
            btnStaticEcuReset.IsEnabled = false;
            AppendLog("[Camera] Performing ECU Reset...");

            try
            {
                _client.CurrentEcuType = EcuType.Camera;
                await Task.Run(() => _client.EcuReset());
                AppendLog("[Camera] ECU Reset completed.");
            }
            catch (Exception ex)
            {
                AppendLog($"[Camera] ECU Reset error: {ex.Message}");
            }
            finally
            {
                btnStaticEcuReset.IsEnabled = true;
            }
        }

        private async void btnStaticReadResult_Click(object sender, RoutedEventArgs e)
        {
            btnStaticReadResult.IsEnabled = false;
            AppendLog("[Camera] Reading Calibration Result...");

            try
            {
                _client.CurrentEcuType = EcuType.Camera;
                byte[] result = null;
                await Task.Run(() => result = _client.CameraReadCalibrationResult());
                AppendLog($"[Camera] Calibration Result: {BitConverter.ToString(result ?? new byte[0])}");
            }
            catch (Exception ex)
            {
                AppendLog($"[Camera] Read Result error: {ex.Message}");
            }
            finally
            {
                btnStaticReadResult.IsEnabled = true;
            }
        }

        private async void btnStaticClearDtc_Click(object sender, RoutedEventArgs e)
        {
            btnStaticClearDtc.IsEnabled = false;
            AppendLog("[Camera] Clearing DTC...");

            try
            {
                _client.CurrentEcuType = EcuType.Camera;
                await Task.Run(() => _client.ClearDTC());
                AppendLog("[Camera] DTC cleared.");
            }
            catch (Exception ex)
            {
                AppendLog($"[Camera] Clear DTC error: {ex.Message}");
            }
            finally
            {
                btnStaticClearDtc.IsEnabled = true;
            }
        }

        private async void btnFinalDtcCheck_Click(object sender, RoutedEventArgs e)
        {
            btnFinalDtcCheck.IsEnabled = false;
            AppendLog("[Camera] Final DTC Check...");

            try
            {
                _client.CurrentEcuType = EcuType.Camera;
                await Task.Run(() => _client.CheckDTC());
                AppendLog("[Camera] Final DTC Check completed.");
            }
            catch (Exception ex)
            {
                AppendLog($"[Camera] Final DTC Check error: {ex.Message}");
            }
            finally
            {
                btnFinalDtcCheck.IsEnabled = true;
            }
        }

        private async void btnStaticAll_Click(object sender, RoutedEventArgs e)
        {
            btnStaticAll.IsEnabled = false;
            AppendLog("=== [Camera] Starting Static Calibration (EOL) - Full Sequence ===");

            try
            {
                _client.CurrentEcuType = EcuType.Camera;
                byte[] vehicleData = GetVehicleDataFromUI();
                await Task.Run(() => _client.CameraStaticCalibrationEOL(vehicleData));
                AppendLog("=== [Camera] Static Calibration (EOL) completed ===");
            }
            catch (Exception ex)
            {
                AppendLog($"[Camera] Static Calibration error: {ex.Message}");
            }
            finally
            {
                btnStaticAll.IsEnabled = true;
            }
        }

        #endregion

        #region Camera - Dynamic Calibration Buttons

        private async void btnDynExtendedSession_Click(object sender, RoutedEventArgs e)
        {
            btnDynExtendedSession.IsEnabled = false;
            AppendLog("[Camera] Starting Extended Session...");

            try
            {
                _client.CurrentEcuType = EcuType.Camera;
                await Task.Run(() => _client.EnterExtendedSession());
                AppendLog("[Camera] Extended Session entered.");
            }
            catch (Exception ex)
            {
                AppendLog($"[Camera] Extended Session error: {ex.Message}");
            }
            finally
            {
                btnDynExtendedSession.IsEnabled = true;
            }
        }

        private async void btnDynSecurityAccess_Click(object sender, RoutedEventArgs e)
        {
            btnDynSecurityAccess.IsEnabled = false;
            AppendLog("[Camera] Performing Security Access...");

            try
            {
                _client.CurrentEcuType = EcuType.Camera;
                await Task.Run(() => _client.CameraSecurityAccess());
                AppendLog("[Camera] Security Access successful.");
            }
            catch (Exception ex)
            {
                AppendLog($"[Camera] Security Access error: {ex.Message}");
            }
            finally
            {
                btnDynSecurityAccess.IsEnabled = true;
            }
        }

        private async void btnVehicleDataWrite_Click(object sender, RoutedEventArgs e)
        {
            btnVehicleDataWrite.IsEnabled = false;
            AppendLog("[Camera] Writing Vehicle Data...");

            try
            {
                _client.CurrentEcuType = EcuType.Camera;
                byte[] vehicleData = GetVehicleDataFromUI();
                await Task.Run(() => _client.CameraVehicleDataWrite(vehicleData));
                AppendLog("[Camera] Vehicle Data Write completed.");
            }
            catch (Exception ex)
            {
                AppendLog($"[Camera] Vehicle Data Write error: {ex.Message}");
            }
            finally
            {
                btnVehicleDataWrite.IsEnabled = true;
            }
        }

        private async void btnDynEcuReset1_Click(object sender, RoutedEventArgs e)
        {
            btnDynEcuReset1.IsEnabled = false;
            AppendLog("[Camera] Performing ECU Reset...");

            try
            {
                _client.CurrentEcuType = EcuType.Camera;
                await Task.Run(() => _client.EcuReset());
                AppendLog("[Camera] ECU Reset completed.");
            }
            catch (Exception ex)
            {
                AppendLog($"[Camera] ECU Reset error: {ex.Message}");
            }
            finally
            {
                btnDynEcuReset1.IsEnabled = true;
            }
        }

        private async void btnDynamicCalibration_Click(object sender, RoutedEventArgs e)
        {
            btnDynamicCalibration.IsEnabled = false;
            AppendLog("[Camera] Starting Dynamic Calibration Routine...");

            try
            {
                _client.CurrentEcuType = EcuType.Camera;
                await Task.Run(() => _client.CameraDynamicCalibrationRoutine());
                AppendLog("[Camera] Dynamic Calibration Routine completed.");
            }
            catch (Exception ex)
            {
                AppendLog($"[Camera] Dynamic Calibration error: {ex.Message}");
            }
            finally
            {
                btnDynamicCalibration.IsEnabled = true;
            }
        }

        private async void btnDynEcuReset2_Click(object sender, RoutedEventArgs e)
        {
            btnDynEcuReset2.IsEnabled = false;
            AppendLog("[Camera] Performing ECU Reset...");

            try
            {
                _client.CurrentEcuType = EcuType.Camera;
                await Task.Run(() => _client.EcuReset());
                AppendLog("[Camera] ECU Reset completed.");
            }
            catch (Exception ex)
            {
                AppendLog($"[Camera] ECU Reset error: {ex.Message}");
            }
            finally
            {
                btnDynEcuReset2.IsEnabled = true;
            }
        }

        private async void btnDynamicReadResult_Click(object sender, RoutedEventArgs e)
        {
            btnDynamicReadResult.IsEnabled = false;
            AppendLog("[Camera] Reading Calibration Result...");

            try
            {
                _client.CurrentEcuType = EcuType.Camera;
                byte[] result = null;
                await Task.Run(() => result = _client.CameraReadCalibrationResult());
                AppendLog($"[Camera] Calibration Result: {BitConverter.ToString(result ?? new byte[0])}");
            }
            catch (Exception ex)
            {
                AppendLog($"[Camera] Read Result error: {ex.Message}");
            }
            finally
            {
                btnDynamicReadResult.IsEnabled = true;
            }
        }

        private async void btnDynamicClearDtc_Click(object sender, RoutedEventArgs e)
        {
            btnDynamicClearDtc.IsEnabled = false;
            AppendLog("[Camera] Clearing DTC...");

            try
            {
                _client.CurrentEcuType = EcuType.Camera;
                await Task.Run(() => _client.ClearDTC());
                AppendLog("[Camera] DTC cleared.");
            }
            catch (Exception ex)
            {
                AppendLog($"[Camera] Clear DTC error: {ex.Message}");
            }
            finally
            {
                btnDynamicClearDtc.IsEnabled = true;
            }
        }

        private async void btnDynamicAll_Click(object sender, RoutedEventArgs e)
        {
            btnDynamicAll.IsEnabled = false;
            AppendLog("=== [Camera] Starting Dynamic Calibration - Full Sequence ===");

            try
            {
                _client.CurrentEcuType = EcuType.Camera;
                byte[] vehicleData = GetVehicleDataFromUI();
                await Task.Run(() => _client.CameraDynamicCalibration(vehicleData));
                AppendLog("=== [Camera] Dynamic Calibration completed ===");
            }
            catch (Exception ex)
            {
                AppendLog($"[Camera] Dynamic Calibration error: {ex.Message}");
            }
            finally
            {
                btnDynamicAll.IsEnabled = true;
            }
        }

        #endregion

        #region Radar EOL Buttons (Y526316)

        private async void btnRadarExtendedSession_Click(object sender, RoutedEventArgs e)
        {
            btnRadarExtendedSession.IsEnabled = false;
            AppendLog("[Radar] Starting Extended Session...");

            try
            {
                _client.CurrentEcuType = EcuType.Radar;
                await Task.Run(() => _client.EnterExtendedSession());
                AppendLog("[Radar] Extended Session entered.");
            }
            catch (Exception ex)
            {
                AppendLog($"[Radar] Extended Session error: {ex.Message}");
            }
            finally
            {
                btnRadarExtendedSession.IsEnabled = true;
            }
        }

        private async void btnRadarDtcCheck_Click(object sender, RoutedEventArgs e)
        {
            btnRadarDtcCheck.IsEnabled = false;
            AppendLog("[Radar] Checking DTC...");

            try
            {
                _client.CurrentEcuType = EcuType.Radar;
                await Task.Run(() => _client.CheckDTC());
                AppendLog("[Radar] DTC Check completed.");
            }
            catch (Exception ex)
            {
                AppendLog($"[Radar] DTC Check error: {ex.Message}");
            }
            finally
            {
                btnRadarDtcCheck.IsEnabled = true;
            }
        }

        private async void btnRadarEOLAlignment_Click(object sender, RoutedEventArgs e)
        {
            btnRadarEOLAlignment.IsEnabled = false;
            AppendLog("[Radar] Starting EOL Alignment ($FFA0)...");

            try
            {
                _client.CurrentEcuType = EcuType.Radar;
                bool result = false;
                await Task.Run(() => result = _client.RadarEOLAlignment());
                AppendLog($"[Radar] EOL Alignment {(result ? "PASSED" : "FAILED")}.");
            }
            catch (Exception ex)
            {
                AppendLog($"[Radar] EOL Alignment error: {ex.Message}");
            }
            finally
            {
                btnRadarEOLAlignment.IsEnabled = true;
            }
        }

        private async void btnRadarEcuReset_Click(object sender, RoutedEventArgs e)
        {
            btnRadarEcuReset.IsEnabled = false;
            AppendLog("[Radar] Performing ECU Reset...");

            try
            {
                _client.CurrentEcuType = EcuType.Radar;
                await Task.Run(() => _client.EcuReset());
                AppendLog("[Radar] ECU Reset completed.");
            }
            catch (Exception ex)
            {
                AppendLog($"[Radar] ECU Reset error: {ex.Message}");
            }
            finally
            {
                btnRadarEcuReset.IsEnabled = true;
            }
        }

        private async void btnRadarReadAlignData_Click(object sender, RoutedEventArgs e)
        {
            btnRadarReadAlignData.IsEnabled = false;
            AppendLog("[Radar] Reading Alignment Data ($FEA7)...");

            try
            {
                _client.CurrentEcuType = EcuType.Radar;
                byte[] alignData = null;
                await Task.Run(() => alignData = _client.RadarReadAlignmentData());
                AppendLog($"[Radar] Alignment Data ({alignData?.Length ?? 0} bytes): {BitConverter.ToString(alignData ?? new byte[0])}");
            }
            catch (Exception ex)
            {
                AppendLog($"[Radar] Read Alignment Data error: {ex.Message}");
            }
            finally
            {
                btnRadarReadAlignData.IsEnabled = true;
            }
        }

        private async void btnRadarClearDtc_Click(object sender, RoutedEventArgs e)
        {
            btnRadarClearDtc.IsEnabled = false;
            AppendLog("[Radar] Clearing DTC...");

            try
            {
                _client.CurrentEcuType = EcuType.Radar;
                await Task.Run(() => _client.ClearDTC());
                AppendLog("[Radar] DTC cleared.");
            }
            catch (Exception ex)
            {
                AppendLog($"[Radar] Clear DTC error: {ex.Message}");
            }
            finally
            {
                btnRadarClearDtc.IsEnabled = true;
            }
        }

        private async void btnRadarFinalDtcCheck_Click(object sender, RoutedEventArgs e)
        {
            btnRadarFinalDtcCheck.IsEnabled = false;
            AppendLog("[Radar] Final DTC Check...");

            try
            {
                _client.CurrentEcuType = EcuType.Radar;
                await Task.Run(() => _client.CheckDTC());
                AppendLog("[Radar] Final DTC Check completed.");
            }
            catch (Exception ex)
            {
                AppendLog($"[Radar] Final DTC Check error: {ex.Message}");
            }
            finally
            {
                btnRadarFinalDtcCheck.IsEnabled = true;
            }
        }

        private async void btnRadarAll_Click(object sender, RoutedEventArgs e)
        {
            btnRadarAll.IsEnabled = false;
            AppendLog("=== [Radar] Starting Radar EOL Test - Full Sequence ===");

            try
            {
                _client.CurrentEcuType = EcuType.Radar;
                await Task.Run(() => _client.RadarEOLTest());
                AppendLog("=== [Radar] Radar EOL Test completed ===");
            }
            catch (Exception ex)
            {
                AppendLog($"[Radar] Radar EOL Test error: {ex.Message}");
            }
            finally
            {
                btnRadarAll.IsEnabled = true;
            }
        }

        #endregion

        // Vehicle Data 생성 (Camera용 - DID $FD10)
        // 14 bytes: LeftWheel(2) + RightWheel(2) + DistToHead(2) + LateralToCenter(2) + ChasisNumber(4) + CountryCode(1) + Status(1)
        private byte[] GetVehicleDataFromUI()
        {
            // 기본값 - 추후 UI 입력으로 대체
            return new byte[14] {
                0x00, 0x64,  // Left Wheel: 100cm
                0x00, 0x64,  // Right Wheel: 100cm
                0x00, 0x50,  // Distance To Head: 80cm
                0x00, 0x00,  // Lateral to Center: 0cm
                0x00, 0x00, 0x00, 0x01,  // ChasisNumber: 1
                0x01,        // CountryCode: China
                0xAA         // Status: Programmed
            };
        }

        private void btnClearLog_Click(object sender, RoutedEventArgs e)
        {
            txtLog.Clear();
        }

        protected override void OnClosed(EventArgs e)
        {
            _client?.Dispose();
            base.OnClosed(e);
        }
    }
}

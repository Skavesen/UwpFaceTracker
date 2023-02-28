using OpenCvSharp;
using Serilog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Enumeration;
using Windows.Graphics.Imaging;
using Windows.Media;
using Windows.Media.Capture;
using Windows.Media.Devices;
using Windows.Media.FaceAnalysis;
using Windows.Media.MediaProperties;
using Windows.Storage.Streams;
using Windows.System.Threading;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace UwpFaceTracker
{
    public sealed partial class GridForVideoPreview : UserControl
    {
        public event EventHandler LostFace;
        public event EventHandler FoundFace;
        public event EventHandler LostFaceDelayed;
        public event EventHandler FoundFaceDelayed;
        public event EventHandler FaceAreaXChanged;
        public event EventHandler FaceAreaYChanged;
        public bool HasFace { get; set; }
        public bool HasBigFace { get; set; }
        public int FaceX { get; set; }
        public int FaceY { get; set; }
        public int FaceXPrev { get; set; }
        public int FaceYPrev { get; set; }
        private int _numberDroppedFrames = 0;
        FaceData faceData;

        private MediaCapture _mediaCapture;
        public IMediaEncodingProperties PreviewProperties { get; set; }
        public async Task<DeviceInformation> FindCameraDeviceByPanelAsync(Windows.Devices.Enumeration.Panel desiredPanel)
        {
            var allVideoDevices = await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture);
            DeviceInformation desiredDevice = allVideoDevices.FirstOrDefault(x => x.EnclosureLocation != null && x.EnclosureLocation.Panel == desiredPanel);
            return desiredDevice ?? allVideoDevices.FirstOrDefault();
        }
        public async Task<bool> InitializeCameraAsync()
        {
            bool _isCameraInitialized = false;

            if (_mediaCapture == null)
            {
                var cameraDevice = await FindCameraDeviceByPanelAsync(Windows.Devices.Enumeration.Panel.Front);

                if (cameraDevice == null)
                {
                    _logger.Warning("No camera device found!");
                }

                _mediaCapture = new MediaCapture();

                try
                {
                    await _mediaCapture.InitializeAsync();
                    _isCameraInitialized = true;
                }
                catch (UnauthorizedAccessException ex)
                {
                    _logger.Warning("The app was denied access to the camera");
                }
            }

            return _isCameraInitialized;
        }
        public async Task StartPreviewAsync(int rotationCamera)
        {
            _mediaCapture.VideoDeviceController.DesiredOptimization = MediaCaptureOptimization.Quality;

            var streamProperties = _mediaCapture.VideoDeviceController.GetAvailableMediaStreamProperties(MediaStreamType.VideoPreview).Select(x => new StreamProperties(x));

            var desiredProperty = streamProperties.FirstOrDefault(x => x.Width == 1920 && x.Height == 1080 && x.FrameRate == 30);


            await _mediaCapture.VideoDeviceController.SetMediaStreamPropertiesAsync(MediaStreamType.VideoPreview, desiredProperty.EncodingProperties);

            PreviewProperties = _mediaCapture.VideoDeviceController.GetMediaStreamProperties(MediaStreamType.VideoPreview);
            if (rotationCamera == 0)
            {
                _mediaCapture.SetPreviewRotation(VideoRotation.None);
            }
            else if (rotationCamera == 0 || rotationCamera == 180)
            {
                outer.Width = 1280;
                outer.Height = 720;
                outer.Margin = new Thickness(-280, 0, 0, 0);
                videoStream.Width = 1920;
                videoStream.Height = 1080;
                videoStream.Margin = new Thickness(0, 0, 0, 0);
            }
            else if (rotationCamera == 90)
            {

                _mediaCapture.SetPreviewRotation(VideoRotation.Clockwise90Degrees);
            }
            else if (rotationCamera == 90 || rotationCamera == 270)
            {
                outer.Width = 720;
                outer.Height = 1280;
                outer.Margin = new Thickness(0, 0, 0, 0);
                videoStream.Width = 3420;
                videoStream.Height = 1920;
                videoStream.Margin = new Thickness(-1170, 0, 0, 0);
            }
            else if (rotationCamera == 180)
            {
                _mediaCapture.SetPreviewRotation(VideoRotation.Clockwise90Degrees);
            }
            else if (rotationCamera == 270)
            {
                _mediaCapture.SetPreviewRotation(VideoRotation.Clockwise90Degrees);
            }

            videoStream.Source = _mediaCapture;
            await _mediaCapture.StartPreviewAsync();
        }

        public async Task StopPreviewAsync()
        {
            videoStream.Source = null;
            await _mediaCapture.StopPreviewAsync();
        }

        private int rotationCamera = 0;
        int takeFotoMs = 100;

        private readonly ILogger _logger = Log.Logger.ForContext<GridForVideoPreview>();

        private FaceTracker faceTracker;
        private readonly SemaphoreSlim frameProcessingSemaphore = new SemaphoreSlim(1);
        private SoftwareBitmap _bestSoftwareBitmap;
        private double _blurCoefficient = 0;
        private BlockingCollection<BitmapBounds> _boundsFaces = new BlockingCollection<BitmapBounds>();
        private bool _frameProcessed;
        private double _minFaceWidth, _minFaceHeight, _coefficientFrameSize, _minNearFaceWidth, _minNearFaceHeight;


        public event EventHandler<BitmapBounds> TrackNearFace;
        public event EventHandler<string> FacesSaved;
        public event EventHandler<bool> FoundFaceFarAway;
        private ThreadPoolTimer threadTimer;

        public BlockingCollection<DetectedFace> DetectedFaces { get; set; } = new BlockingCollection<DetectedFace>();

        public bool IsFullFrame { get; set; }

        public GridForVideoPreview()
        {
            this.InitializeComponent();

        }
        public async Task RunAync(MediaCapture mediaCapture, double minFaceWidth, double minFaceHeight, double coefficientFrameSize, double minNearFaceWidth, double minNearFaceHeight)
        {
            _mediaCapture = mediaCapture;
            faceTracker = await FaceTracker.CreateAsync();
            _minFaceWidth = minFaceWidth;
            _minFaceHeight = minFaceHeight;
            _coefficientFrameSize = coefficientFrameSize;
            _minNearFaceWidth = minNearFaceWidth;
            _minNearFaceHeight = minNearFaceHeight;

            HasFace = false;
            HasBigFace = false;
            IsFullFrame = false;
            DetectedFaces = new BlockingCollection<DetectedFace>();
            _frameProcessed = true;

            TimeSpan timerInterval = TimeSpan.FromMilliseconds(takeFotoMs);
            //threadTimer = ThreadPoolTimer.CreatePeriodicTimer(new TimerElapsedHandler(ProcessCurrentVideoFrame), timerInterval);
        }

        /// <summary>
        /// Подсветка бОльшего лица рамкой (передаем UI-элемент)
        /// </summary>
        public void StartTrackFace(UIElement FaceFrame, int MinFaceSize)
        {
        }
        /// <summary>
        /// Получение фото с заданным блар и таймаутом
        /// </summary>
        /// <param name="BlurThreshold">порог размытия</param>
        /// <param name="TryCounter">счетчик-ограничитель</param>
        /// <param name="MinFaceSize">минимальный размер лица</param>
        /// <returns></returns>
        public async Task<FaceData> GetFacePhoto(double BlurThreshold, int TryCounter, int MinFaceSize)
        {
            faceData = null;
            var previewProperties = _mediaCapture.VideoDeviceController.GetMediaStreamProperties(MediaStreamType.VideoPreview) as VideoEncodingProperties;

            using (var videoFrame = new VideoFrame(BitmapPixelFormat.Nv12, (int)previewProperties.Width, (int)previewProperties.Height))
            {
                using (var currentFrame = await _mediaCapture.GetPreviewFrameAsync(videoFrame))
                {
                    const BitmapPixelFormat faceDetectionPixelFormat = BitmapPixelFormat.Nv12;

                    if (currentFrame.SoftwareBitmap.BitmapPixelFormat != faceDetectionPixelFormat)
                        return null;

                    try
                    {
                        IList<DetectedFace> detectedFaces = await faceTracker.ProcessNextFrameAsync(currentFrame);

                        if (detectedFaces.Count > 0)
                        {
                            var maxFaceWidth = detectedFaces.Max(x => x.FaceBox.Width);
                            var face = detectedFaces.FirstOrDefault(x => x.FaceBox.Width == maxFaceWidth);
                            if (face.FaceBox.Width < MinFaceSize)
                            {
                                HasFace = true;
                                HasBigFace = false;
                                _numberDroppedFrames = 0;
                            }

                            else if (face.FaceBox.Width > MinFaceSize)
                            {
                                HasFace = true;
                                HasBigFace = true;
                                _numberDroppedFrames++;
                                if (_frameProcessed)
                                {
                                    await Task.Factory.StartNew(async () =>
                                    {
                                        _frameProcessed = false;
                                        try
                                        {
                                            var softwareBitmap = currentFrame.SoftwareBitmap;

                                            _boundsFaces = new BlockingCollection<BitmapBounds>();
                                            _boundsFaces.Add(detectedFaces[0].FaceBox);
                                            foreach (var _faceBounds in _boundsFaces)
                                            {
                                                var encoded = new InMemoryRandomAccessStream();

                                                BitmapEncoder encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, encoded);

                                                var convertedSoftwareBitmap = SoftwareBitmap.Convert(softwareBitmap, BitmapPixelFormat.Rgba16);

                                                encoder.SetSoftwareBitmap(convertedSoftwareBitmap);
                                                _logger.Information($"_faceBound {_faceBounds.Width}x{_faceBounds.Height}");
                                                encoder.BitmapTransform.Bounds = GetEnlargedFaceArea(_faceBounds);
                                                _logger.Information($"Bound {encoder.BitmapTransform.Bounds.Width}x{encoder.BitmapTransform.Bounds.Height}");

                                                encoder.IsThumbnailGenerated = true;
                                                await encoder.FlushAsync();
                                                encoded.Seek(0);

                                                var bytes = new byte[encoded.Size];
                                                await encoded.AsStream().ReadAsync(bytes, 0, bytes.Length);
                                                var base64image = Convert.ToBase64String(bytes);

                                                using (Mat src = Mat.FromImageData(bytes, ImreadModes.Grayscale))
                                                {
                                                    var newBlurCoefficient = CalculateBlurryEffect(src);
                                                    if (newBlurCoefficient >= BlurThreshold)
                                                    {
                                                        faceData.X = _faceBounds.X;
                                                        faceData.Y = _faceBounds.Y;
                                                        faceData.WidthHeight = _faceBounds.Width;
                                                        faceData.base64image = base64image;
                                                    }
                                                }
                                            }
                                        }
                                        catch (Exception ex)
                                        {
                                            _logger.Error(ex, "Face tracking failed");
                                        }
                                        _frameProcessed = true;
                                    });
                                }
                            }
                        }
                        else
                            _numberDroppedFrames++;

                        if (_numberDroppedFrames >= TryCounter)
                        {
                            HasFace = false;
                            ResetValuesOfBestFrame();
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "Face tracking failed");
                    }
                    return faceData;
                }
            }
        }
        /// <summary>
        /// Получение массива всех лиц с баундом, которые видны сейчас, с учетом ограничения размера
        /// </summary>
        /// <param name="TryCounter">счетчик-ограничитель</param>
        /// <returns></returns>
        public async Task<List<FaceData>> GetAllFaces(int TryCounter)
        {
            faceData = null;
            var previewProperties = _mediaCapture.VideoDeviceController.GetMediaStreamProperties(MediaStreamType.VideoPreview) as VideoEncodingProperties;

            using (var videoFrame = new VideoFrame(BitmapPixelFormat.Nv12, (int)previewProperties.Width, (int)previewProperties.Height))
            {
                using (var currentFrame = await _mediaCapture.GetPreviewFrameAsync(videoFrame))
                {
                    const BitmapPixelFormat faceDetectionPixelFormat = BitmapPixelFormat.Nv12;

                    if (currentFrame.SoftwareBitmap.BitmapPixelFormat != faceDetectionPixelFormat)
                        return null;

                    try
                    {
                        IList<DetectedFace> detectedFaces = await faceTracker.ProcessNextFrameAsync(currentFrame);

                        if (detectedFaces.Count > 0)
                        {
                            HasFace = true;
                            HasBigFace = true;
                            _numberDroppedFrames++;
                            if (_frameProcessed)
                            {
                                await Task.Factory.StartNew(async () =>
                                {
                                    _frameProcessed = false;
                                    try
                                    {
                                        var softwareBitmap = currentFrame.SoftwareBitmap;

                                        _boundsFaces = new BlockingCollection<BitmapBounds>();
                                        foreach (var detectedface in detectedFaces)
                                        {
                                            _boundsFaces.Add(detectedface.FaceBox);
                                        }
                                        foreach (var _faceBounds in _boundsFaces)
                                        {
                                            var encoded = new InMemoryRandomAccessStream();

                                            BitmapEncoder encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, encoded);

                                            var convertedSoftwareBitmap = SoftwareBitmap.Convert(softwareBitmap, BitmapPixelFormat.Rgba16);

                                            encoder.SetSoftwareBitmap(convertedSoftwareBitmap);
                                            _logger.Information($"_faceBound {_faceBounds.Width}x{_faceBounds.Height}");
                                            encoder.BitmapTransform.Bounds = GetEnlargedFaceArea(_faceBounds);
                                            _logger.Information($"Bound {encoder.BitmapTransform.Bounds.Width}x{encoder.BitmapTransform.Bounds.Height}");

                                            encoder.IsThumbnailGenerated = true;
                                            await encoder.FlushAsync();
                                            encoded.Seek(0);

                                            var bytes = new byte[encoded.Size];
                                            await encoded.AsStream().ReadAsync(bytes, 0, bytes.Length);
                                            var base64image = Convert.ToBase64String(bytes);
                                            faceData.X = _faceBounds.X;
                                            faceData.Y = _faceBounds.Y;
                                            faceData.WidthHeight = _faceBounds.Width;
                                            faceData.base64image = base64image;
                                            faceData._faceDataList.Add(faceData);
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        _logger.Error(ex, "Face tracking failed");
                                    }
                                    _frameProcessed = true;
                                });
                            }
                        }
                        else
                            _numberDroppedFrames++;

                        if (_numberDroppedFrames >= TryCounter)
                        {
                            HasFace = false;
                            ResetValuesOfBestFrame();
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "Face tracking failed");
                    }
                    return faceData._faceDataList;
                }
            }
        }
        /// <summary>
        /// Метод очистки коллекции лиц, когда оно пропало из видимости
        /// </summary>
        private void CleanDetectedFaces()
        {
            DetectedFaces = new BlockingCollection<DetectedFace>();
        }
        /// <summary>
        /// Метод  расчёта размытия фото
        /// </summary>
        private double CalculateBlurryEffect(Mat src)
        {
            using (Mat dst = new Mat())
            {
                Cv2.Laplacian(src, dst, MatType.CV_64FC1);
                Cv2.MeanStdDev(dst, out var _, out var stddev);
                return stddev.Val0 * stddev.Val0;
            }
        }
        /// <summary>
        /// Передодируем SoftwareBitmap в массив байт
        /// </summary>
        private async Task<byte[]> EncodedBytes(SoftwareBitmap soft, Guid encoderId)
        {
            byte[] array = null;

            using (var ms = new InMemoryRandomAccessStream())
            {
                BitmapEncoder encoder = await BitmapEncoder.CreateAsync(encoderId, ms);
                var convertedSoftwareBitmap = SoftwareBitmap.Convert(soft, BitmapPixelFormat.Rgba16);
                encoder.SetSoftwareBitmap(convertedSoftwareBitmap);

                try
                {
                    await encoder.FlushAsync();
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Error in encoding bytes");
                    return new byte[0];
                }

                array = new byte[ms.Size];
                await ms.ReadAsync(array.AsBuffer(), (uint)ms.Size, InputStreamOptions.None);
            }
            return array;
        }
        /// <summary>
        /// Наличие лица сейчас больше указанного размера 
        /// </summary>
        private void OnHasFaceChanged()
        {
            if (HasFace)
            {
                FoundFace?.Invoke(this, null);
            }
            else
            {
                LostFace?.Invoke(this, null);
            }
        }
        /// <summary>
        /// Наличие лица больше указанного размера с учетом задержек (наличие человека)
        /// </summary>
        private void OnHasBigFaceChangedDelayed()
        {
            if (HasBigFace)
            {
                FoundFaceDelayed?.Invoke(this, null);
            }
            else
            {
                CleanDetectedFaces();
                LostFaceDelayed?.Invoke(this, null);
            }
        }
        /// <summary>
        /// Наличие лица больше указанного размера с учетом задержек (наличие человека)
        /// </summary>
        private void OnAreaChanged()
        {
            if (FaceX != FaceXPrev)
            {
                FaceXPrev = FaceX;
                FaceAreaXChanged?.Invoke(this, null);
            }
            else if (FaceY != FaceYPrev)
            {
                FaceYPrev = FaceY;
                FaceAreaYChanged?.Invoke(this, null);
            }
        }

        /// <summary>
        /// Метод очистки всех значений
        /// </summary>
        private void ResetValuesOfBestFrame()
        {
            _bestSoftwareBitmap = null;
            _blurCoefficient = 0;
            _boundsFaces = new BlockingCollection<BitmapBounds>();
        }
        /// <summary>
        /// Метод увеличения области вырезаемого лица
        /// </summary>
        private BitmapBounds GetEnlargedFaceArea(BitmapBounds _faceBounds)
        {
            var width = _faceBounds.Width * _coefficientFrameSize;
            var height = _faceBounds.Height * _coefficientFrameSize;
            var x = _faceBounds.X - (width - _faceBounds.Width) / 2;
            var y = _faceBounds.Y - (height - _faceBounds.Height) / 2;

            var faceBox = new BitmapBounds
            {
                Width = (uint)width,
                Height = (uint)height,
                X = (uint)x,
                Y = (uint)y
            };

            var lengthXW = faceBox.X + faceBox.Width;
            if (1920 < lengthXW)
            {
                var diffWidth = lengthXW - 1920;
                faceBox.X -= diffWidth;
            }

            if (0 > faceBox.X)
                faceBox.X = 0;

            var lengthYH = faceBox.Y + faceBox.Height;
            if (1080 < lengthYH)
            {
                var diffHeight = lengthYH - 1080;
                faceBox.Y -= diffHeight;
            }

            if (0 > faceBox.Y)
                faceBox.Y = 0;

            return faceBox;
        }
    }

}

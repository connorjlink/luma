using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.UI;

namespace luma;

public partial class LumaViewModel : INotifyPropertyChanged
{
	public static readonly string ERROR_GLYPH = "\uE783";
	public static readonly string WARNING_GLYPH = "\uE7BA";
	public static readonly string INFO_GLYPH = "\uE946";
	public static readonly string REFRESH_GLYPH = "\uE72C";
	public static readonly string CPU_GLYPH = "\uE950";
	public static readonly string RAM_GLYPH = "\uEEA0";
	public static readonly string MESSAGE_GLYPH = "\uE8BD";
	public static readonly string SETTINGS_GLYPH = "\uF8B0";
	public static readonly string HEALTH_GLYPH = "\uE9D9";
	public static readonly string SPEED_GLYPH = "\uEC4A";
	public static readonly string LIGHTNING_GLYPH = "\uE945";
	public static readonly string CLOCK_GLYPH = "\uE823";

	private static readonly string SHARED_MEM_NAME = "LumaFramebuffer";

	private static readonly RenderMode[] _renderModesAll = [RenderMode.Raytrace, RenderMode.Pathtrace];
	public static readonly List<string> RenderModes = [.. _renderModesAll.Select(m => m.ToString().ToLower())];

	private static readonly uint DEFAULT_WIDTH = 700;
	private static readonly uint DEFAULT_HEIGHT = 500;
	private static readonly uint DEFAULT_SAMPLES = 1;
	private static readonly uint DEFAULT_BOUNCES = 2;
	private static readonly string DEFAULT_MODE = RenderModes.First();
	private static readonly bool DEFAULT_ACCUMULATION = false;

	// rgba format format
	private uint _FramebufferSize => Width * Height * 4;

	private byte[] ReadFramebuffer()
	{
		using var mmf = MemoryMappedFile.OpenExisting(SHARED_MEM_NAME, MemoryMappedFileRights.Read);
		using var accessor = mmf.CreateViewAccessor(0, _FramebufferSize, MemoryMappedFileAccess.Read);
		byte[] buffer = new byte[_FramebufferSize];
		accessor.ReadArray(0, buffer, 0, buffer.Length);
		return buffer;
	}

	public WriteableBitmap? FramebufferBitmap = null;

	/*private void ComposeBitmap()
	{
		if (FramebufferBitmap is null)
		{
			PostNotification(ERROR_GLYPH, "Failed to initialize the framebuffer", "Error initializing the framebuffer bitmap");
			return;
		}

		try
		{
			using var mmf = MemoryMappedFile.OpenExisting(SHARED_MEM_NAME, MemoryMappedFileRights.Read);
			using var accessor = mmf.CreateViewAccessor(0, _FramebufferSize, MemoryMappedFileAccess.Read);

			var pixelBuffer = FramebufferBitmap.PixelBuffer;
			byte[]? buffer = WindowsRuntimeBufferExtensions.ToArray(pixelBuffer, 0, (int)_FramebufferSize);

			// Si el buffer es válido, copia directamente desde la memoria compartida
			if (buffer != null)
			{
				accessor.ReadArray(0, buffer, 0, buffer.Length);

				// Escribe el buffer actualizado de vuelta al PixelBuffer
				pixelBuffer.AsStream().Seek(0, SeekOrigin.Begin);
				pixelBuffer.AsStream().Write(buffer, 0, buffer.Length);
			}

			ClearError();
		}
		catch (Exception ex)
		{
			PostNotification(ERROR_GLYPH, "Failed to blit the framebuffer", $"Error composing the framebuffer bitmap: {ex.Message}");
		}
	}*/

	private MemoryMappedFile? _mmf;
	private MemoryMappedViewAccessor? _accessor;

	private void InitializeSharedMemory()
	{
		_mmf = MemoryMappedFile.OpenExisting(SHARED_MEM_NAME, MemoryMappedFileRights.Read);
		_accessor = _mmf.CreateViewAccessor(0, _FramebufferSize, MemoryMappedFileAccess.Read);
	}

	private void DisposeSharedMemory()
	{
		_accessor?.Dispose();
		_mmf?.Dispose();
	}

	private unsafe void ComposeBitmap()
	{
		if (FramebufferBitmap is null)
			return;

		try
		{
			using var mmf = MemoryMappedFile.OpenExisting(SHARED_MEM_NAME, MemoryMappedFileRights.Read);
			using var accessor = mmf.CreateViewAccessor(0, _FramebufferSize, MemoryMappedFileAccess.Read);

			byte* srcPtr = null;
			accessor.SafeMemoryMappedViewHandle.AcquirePointer(ref srcPtr);

			try
			{
				using var pixelStream = FramebufferBitmap.PixelBuffer.AsStream();
				pixelStream.Seek(0, SeekOrigin.Begin);

				// Allocate temp buffer ONCE and reuse (better yet: use stackalloc if size is small enough)
				Span<byte> span = new(srcPtr, (int)_FramebufferSize);
				pixelStream.Write(span);
			}
			finally
			{
				accessor.SafeMemoryMappedViewHandle.ReleasePointer();
			}
		}
		catch (Exception ex)
		{
			PostNotification(ERROR_GLYPH, "Framebuffer Error", ex.Message);
		}
	}


	private PerformanceCounter? _cpuCounter;
	private DispatcherTimer? _resourceTimer;
	private Process? _renderProcess;

	private void StartPerformanceCounter()
	{
		try
		{
			_cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
			_cpuCounter.NextValue();

			_resourceTimer = new DispatcherTimer();
			_resourceTimer.Interval = TimeSpan.FromSeconds(1);
			_resourceTimer.Tick += UpdateResourceUsage;
			_resourceTimer.Start();
		}
		catch (Exception ex)
		{
			PostNotification(ERROR_GLYPH, "Failed to initialize the performance monitor", $"Error initializing the performance counter: {ex.Message}");
		}
	}

	private void StopPerformanceCounter()
	{
		if (_resourceTimer is not null)
		{
			_resourceTimer.Stop();
			_resourceTimer.Tick -= UpdateResourceUsage;
			_resourceTimer = null;
			PostNotification(INFO_GLYPH, "Stopped the performance counter", "Stopped polling performance metrics from the render process");
		}
	}

	private readonly Microsoft.UI.Dispatching.DispatcherQueue _uiDispatcherQueue;

	public LumaViewModel()
	{
		_uiDispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
		FramebufferBitmap = new WriteableBitmap((int)Width, (int)Height);

		StartPerformanceCounter();
		FetchUserAccentColor();
	}


	private uint _width = DEFAULT_WIDTH;
	private uint _realWidth = DEFAULT_WIDTH;
	public uint Width 
	{ 
		get => _width;
		set
		{
			if (_width != value)
			{
				_width = value;
				OnPropertyChanged(nameof(Width));
				OnPropertyChanged(nameof(IsSettingModified));
			}
		}
	}

	private uint _height = 500;
	private uint _realHeight = DEFAULT_HEIGHT;
	public uint Height
	{ 
		get => _height;
		set
		{
			if (_height != value)
			{
				_height = value;
				OnPropertyChanged(nameof(Height));
				OnPropertyChanged(nameof(IsSettingModified));
			}
		}
	}

	private uint _samples = DEFAULT_SAMPLES;
	private uint _realSamples = DEFAULT_SAMPLES;
	public uint Samples 
	{
		get => _samples;
		set
		{
			if (_samples != value)
			{
				_samples = value;
				OnPropertyChanged(nameof(Samples));
				OnPropertyChanged(nameof(IsSettingModified));
			}
		}
	}

	private uint _bounces = DEFAULT_BOUNCES;
	private uint _realBounces = DEFAULT_BOUNCES;
	public uint Bounces
	{
		get => _bounces;
		set
		{
			if (_bounces != value)
			{
				_bounces = value;
				OnPropertyChanged(nameof(Bounces));
				OnPropertyChanged(nameof(IsSettingModified));
			}
		}
	}


	private enum RenderMode
	{
		Raytrace,
		Pathtrace,
	}

	private string _mode = DEFAULT_MODE;
	private string _realMode = DEFAULT_MODE;
	public string Mode
	{
		get => _mode;
		set
		{
			if (_mode != value)
			{
				_mode = value;
				OnPropertyChanged(nameof(Mode));
				OnPropertyChanged(nameof(IsSettingModified));
			}
		}
	}

	private bool _isAccumulationEnabled = DEFAULT_ACCUMULATION;
	private bool _realIsAccumulationEnabled = DEFAULT_ACCUMULATION;
	public bool IsAccumulationEnabled
	{
		get => _isAccumulationEnabled;
		set
		{
			_isAccumulationEnabled = value;
			OnPropertyChanged(nameof(IsAccumulationEnabled));
			OnPropertyChanged(nameof(IsSettingModified));
		}

	}

	public bool IsSettingModified
	{
		get => (_isAccumulationEnabled != _realIsAccumulationEnabled ||
				_bounces != _realBounces || 
				_samples != _realSamples || 
				_width != _realWidth || 
				_height != _realHeight || 
				_mode != _realMode);
	}



	private string _statusIcon = "";
	public string StatusIcon
	{
		get => _statusIcon;
		set
		{
			_statusIcon = value;
			OnPropertyChanged(nameof(StatusIcon));
		}
	}

	private string _statusMessage = "Idle";
	public string StatusMessage
	{
		get => _statusMessage;
		set
		{
			_statusMessage = value;
			OnPropertyChanged(nameof(StatusMessage));
		}
	}

	private string? _statusTooltip;
	public string? StatusTooltip
	{
		get => _statusTooltip;
		set
		{
			_statusTooltip = value;
			OnPropertyChanged(nameof(StatusTooltip));
		}
	}

	private string _statusBarColor = "#FF8F84D5";
	public string StatusBarColor
	{
		get => _statusBarColor;
		set
		{
			_statusBarColor = value;
			OnPropertyChanged(nameof(StatusBarColor));
		}
	}

	private string _cpuUsage = string.Empty;
	public string CpuUsage
	{
		get => _cpuUsage;
	}

	private string _ramUsage = string.Empty;
	public string RamUsage
	{
		get => _ramUsage;
	}

	private string _fillRate = string.Empty;
	public string FillRate
	{
		get => _fillRate;
	}

	private string _frameTime = string.Empty;
	public string FrameTime
	{
		get => _frameTime;
	}

	private string _frameNumber = string.Empty;
	public string FrameNumber
	{
		get => _frameNumber;
	}

	// hide status performance metrics when not connected to the render process
	public bool IsStatusVisible => (!_renderProcess?.HasExited ?? false);

	private void UpdateResourceUsage(object? sender, object e)
	{
		try
		{
			var cpu = $"{_cpuCounter?.NextValue():F2}%";
			var ramUsage = Process.GetCurrentProcess().WorkingSet64;
			var allocationInMB = ramUsage / (1024 * 1024);
			var ram = $"{allocationInMB} MB";

			_cpuUsage = cpu;
			_ramUsage = ram;
			OnPropertyChanged(nameof(CpuUsage));
			OnPropertyChanged(nameof(RamUsage));
		}
		catch (Exception ex)
		{
			PostNotification(ERROR_GLYPH, "Failed to read the resource usage", $"Error reading the resource usage: {ex.Message}");
		}
	}

	public void ClearError()
	{
		StatusIcon = INFO_GLYPH;
		StatusMessage = "Idle";
		StatusTooltip = null;
	}

	public class Notification(string glyph, string friendlyMessage, string systemMessage)
	{
		public string Glyph { get; set; } = glyph;
		public string FriendlyMessage { get; set; } = friendlyMessage;
		public string SystemMessage { get; set; } = systemMessage;
	}

	private List<Notification> _notifications = new List<Notification>();

	public IEnumerable<Notification> Notifications => _notifications
		.OrderByDescending(n => _notifications.IndexOf(n))
		.Take(5);

	public void RemoveNotification(Notification notification)
	{
		_notifications.Remove(notification);
		OnPropertyChanged(nameof(Notifications));
	}

	private void PostNotification(string glyph, string friendlyMessage, string systemMessage)
	{
		_uiDispatcherQueue.TryEnqueue(() =>
		{
			StatusIcon = glyph;
			StatusMessage = friendlyMessage;
			StatusTooltip = systemMessage;
			_notifications.Add(new(glyph, friendlyMessage, systemMessage));
			Console.WriteLine(systemMessage);
		});

		OnPropertyChanged(nameof(StatusMessage));
		OnPropertyChanged(nameof(StatusTooltip));
		OnPropertyChanged(nameof(StatusIcon));
	}

	private void FetchUserAccentColor()
	{
		try
		{
			var accentColor = (Color)Application.Current.Resources["SystemAccentColor"];
			string hexAccent = $"#{accentColor.A:X2}{accentColor.R:X2}{accentColor.G:X2}{accentColor.B:X2}";
			StatusBarColor = hexAccent;
			ClearError();
		}
		catch (Exception ex)
		{
			PostNotification(WARNING_GLYPH, "Failed to fetch user accent color", $"Error fetching user accent color: {ex.Message}");
		}
	}

	private void FrameReady(object sender, DataReceivedEventArgs e)
	{
		if (e.Data is null)
			return;

		if (e.Data.StartsWith("[DELIVERED FRAME]"))
		{
			_uiDispatcherQueue.TryEnqueue(() =>
			{
				ReadStatusInternal(e.Data);
			});
		}
	}

	private void ReadStatusInternal(string? line)
	{
		try
		{
			if (line is null)
				return;

			ComposeBitmap();

			var components = line?.Split(':');
			if (components is null || components.Length < 3)
				return;

			var frameNumber = components[1];

			_frameNumber = frameNumber.Trim();
			OnPropertyChanged(nameof(FrameNumber));

			var ms = components[2];
			var fps = 1000 / float.Parse(ms);

			_frameTime = ms + " ms (" + fps.ToString("F2") + " FPS)";
			OnPropertyChanged(nameof(FrameTime));

			var pixelsPerFrame = _width * _height;
			var pixelsPerSecond = fps * pixelsPerFrame;
			var megapixelsPerSecond = pixelsPerSecond / 1e6f;

			_fillRate = megapixelsPerSecond.ToString("F2") + " MP/s";
			OnPropertyChanged(nameof(FillRate));
		}
		catch (Exception ex)
		{
			PostNotification(ERROR_GLYPH, "Failed to read status", $"Error reading status from the render process: {ex.Message}");
		}
	}

	public void SaveRenderingSettings()
	{
		_realIsAccumulationEnabled = _isAccumulationEnabled;
		_realWidth = _width;
		_realHeight = _height;
		_realSamples = _samples;
		_realBounces = _bounces;
		_realMode = _mode;

		OnPropertyChanged(nameof(IsSettingModified));

		// resize the output bitmap
		FramebufferBitmap = new WriteableBitmap((int)Width, (int)Height);
	}

	private bool _isConnected = false;
	public bool IsConnected
	{
		get => _isConnected;
		set
		{
			_isConnected = value;
			OnPropertyChanged(nameof(IsConnected));
			OnPropertyChanged(nameof(RunStopButtonText));
		}
	}

	public string RunStopButtonText
	{
		get => IsConnected ? "Stop Renderer" : "Start Renderer";
	}


	private void HandleProcessDisconnected(object? sender, EventArgs e)
	{
		IsConnected = false;

		_uiDispatcherQueue.TryEnqueue(() =>
		{
			PostNotification(ERROR_GLYPH, "Disconnected from the render process", "Unexpectedly lost the inter-process communication link with the render process");
		});

		//PostNotification(ERROR_GLYPH, "Disconnected from the render process", "Unexpectedly lost the inter-process communication link with the render process");
	}

	public void StopRenderer()
	{
		if (_renderProcess != null && !_renderProcess.HasExited)
		{
			try
			{
				DisposeSharedMemory();
				StopPerformanceCounter();
				_renderProcess.Exited -= HandleProcessDisconnected;
				_renderProcess.Kill();
				IsConnected = false;
				_renderProcess.Dispose();
			}
			catch (Exception ex)
			{
				PostNotification(ERROR_GLYPH, "Failed to kill the render process", $"Error killing the render process: {ex.Message}");
				return;
			}
		}

		// force kill potential leftovers children in case they break loose
		foreach (var process in Process.GetProcessesByName("raytracer"))
		{
			try
			{
				process.Kill();
			}
			catch (Exception ex)
			{
				PostNotification(LumaViewModel.ERROR_GLYPH, "Failed to kill the render process", $"Failed to kill the render subprocess: {ex.Message}");
			}
		}
	}

	public void StartRenderer()
	{
		string args = $"--width={_realWidth} --height={_realHeight} --samples={_realSamples} --bounces={_realBounces} --mode={_realMode} --context=headless";

		_renderProcess = new Process
		{
			StartInfo = new ProcessStartInfo
			{
				FileName = "C:\\Users\\Connor\\Desktop\\raytracer\\x64\\Release\\raytracer.exe",
				Arguments = args,
				UseShellExecute = false,
				RedirectStandardOutput = true,
				CreateNoWindow = true
			},
			EnableRaisingEvents = true
		};

		try
		{
			_renderProcess.OutputDataReceived += FrameReady;
			_renderProcess.Exited += HandleProcessDisconnected;
			_renderProcess.Start();
			_renderProcess.BeginOutputReadLine();
			InitializeSharedMemory();
			IsConnected = true;
			PostNotification(INFO_GLYPH, "Render process started", $"Render process started with arguments: {args}");
		}
		catch (Exception ex)
		{
			StopRenderer();
			PostNotification(ERROR_GLYPH, "Failed to launch the render process", $"Error starting the render process: {ex.Message}");
		}
	}

	private void RestartRenderProcess()
	{
		StopRenderer();
		StartRenderer();
	}

	public event PropertyChangedEventHandler? PropertyChanged;
	protected void OnPropertyChanged(string propertyName)
	{
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
	}
}

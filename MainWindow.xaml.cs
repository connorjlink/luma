using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Windows.Foundation;
using Windows.Foundation.Collections;
using System.Windows;
using Microsoft.Win32;
using System.Diagnostics;

namespace luma;

public sealed partial class MainWindow : Window
{
	private LumaViewModel ViewModel;

	public void OpenNotificationFlyout(object sender, RoutedEventArgs e)
	{
		if (!ViewModel.Notifications.Any())
		{
			ViewModel.ClearError();
			return;
		}

		var flyout = (MenuFlyout)MainGrid.Resources["NotificationFlyout"];
		flyout.Items.Clear();
		foreach (var notification in ViewModel.Notifications)
		{
			var item = new MenuFlyoutItem
			{
				Text = notification.FriendlyMessage,
				Icon = new FontIcon
				{
					Glyph = notification.Glyph,
					FontSize = 16,
					FontFamily = new FontFamily("Segoe Fluent Icons"),
				}
			};
			item.Click += (s, args) =>
			{
				ViewModel.RemoveNotification(notification);
				OpenNotificationFlyout(sender, e);
			};
			flyout.Items.Add(item);
		}
		flyout.ShowAt(NotificationButton);
	}

	public void RunStopRenderer(object sender, RoutedEventArgs e)
	{
		if (ViewModel.IsConnected)
		{
			ViewModel.StopRenderer();
		}
		else
		{
			ViewModel.StartRenderer();
		}
	}

	public void SaveRenderingSettings(object sender, RoutedEventArgs e)
	{
		ViewModel.SaveRenderingSettings();
	}

	public MainWindow()
	{
		InitializeComponent();
		this.ViewModel = new LumaViewModel();
	}
}

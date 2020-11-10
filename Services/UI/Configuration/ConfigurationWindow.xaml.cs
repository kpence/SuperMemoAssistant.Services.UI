#region License & Metadata

// The MIT License (MIT)
// 
// Permission is hereby granted, free of charge, to any person obtaining a
// copy of this software and associated documentation files (the "Software"),
// to deal in the Software without restriction, including without limitation
// the rights to use, copy, modify, merge, publish, distribute, sublicense,
// and/or sell copies of the Software, and to permit persons to whom the
// Software is furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
// DEALINGS IN THE SOFTWARE.

#endregion




namespace SuperMemoAssistant.Services.UI.Configuration
{
  using System;
  using System.Collections.Generic;
  using System.Collections.ObjectModel;
  using System.ComponentModel;
  using System.Linq;
  using System.Reflection;
  using System.Threading;
  using System.Threading.Tasks;
  using System.Windows;
  using Anotar.Serilog;
  using Extensions;
  using Forge.Forms;
  using IO.HotKeys;
  using SuperMemoAssistant.Extensions;
  using Sys.ComponentModel;
  using Sys.Windows.Input;

  /// <summary>Provides facilities to display a configuration UI</summary>
  public partial class ConfigurationWindow : Window, INotifyPropertyChanged
  {
    #region Constants & Statics

    /// <summary>Ensures only one <see cref="ConfigurationWindow" /> is open at all time</summary>
    private static Semaphore SingletonSemaphore { get; } = new Semaphore(1, 1);

    #endregion




    #region Properties & Fields - Non-Public

    private Dictionary<object, object> ModelOriginalMap { get; } = new Dictionary<object, object>();

    private Dictionary<Type, Dictionary<string, MethodInfo>> TypeMethodsMap { get; } =
      new Dictionary<Type, Dictionary<string, MethodInfo>>();

    #endregion




    #region Constructors

    protected ConfigurationWindow(string title, HotKeyManager hotKeyManager, params INotifyPropertyChanged[] configModels)
    {
      foreach (var original in configModels)
      {
        object model = original;

        if (original.GetType().IsSubclassOfUnboundGeneric(typeof(CfgBase<>)))
          model = MapClone(original);

        ModelOriginalMap[model] = original;
        Models.Add(model);
      }

      if (hotKeyManager != null)
      {
        Models.Add(hotKeyManager);
        HotKeyManager = hotKeyManager;
      }

      Title = string.IsNullOrWhiteSpace(title) == false
        ? title
        : $"{Svc.Plugin.Name} Plugin Settings";

      CancelCommand = new AsyncRelayCommand(CancelChangesAsync, null, HandleExceptionAsync);
      SaveCommand   = new RelayCommand(SaveChanges);
      
      InitializeComponent();
    }

    #endregion




    #region Properties & Fields - Public

    /// <summary>The configuration instances to display and edit</summary>
    public ObservableCollection<object> Models { get; } = new ObservableCollection<object>();

    /// <summary>
    ///   The optional <see cref="HotKeyManager" />. Hotkey rebinding options will be offered to the user if it is set to a
    ///   valid instance.
    /// </summary>
    public HotKeyManager HotKeyManager { get; }

    /// <summary>Optional callback to override the default saving mechanism</summary>
    public Action<INotifyPropertyChanged> SaveMethod { get; set; }

    public AsyncRelayCommand CancelCommand { get; }
    public RelayCommand      SaveCommand   { get; }

    #endregion




    #region Methods

    private void SaveChanges()
    {
      foreach (var kv in ModelOriginalMap)
      {
        var model    = kv.Key;
        var original = kv.Value;

        if (ReferenceEquals(model, original) == false)
          ApplyChanges(original, model);

        switch (original)
        {
          case INotifyPropertyChanged config when SaveMethod != null:
            SaveMethod(config);
            break;

          case INotifyPropertyChangedEx config:
            if (config.IsChanged)
            {
              config.IsChanged = false;
              Svc.Configuration.SaveAsync(original, original.GetType()).RunAsync();
            }

            break;

          case INotifyPropertyChanged _:
            Svc.Configuration.SaveAsync(original, original.GetType()).RunAsync();
            break;
        }
      }

      Close();
    }

    private async Task CancelChangesAsync()
    {
      if (HasChanges() == false)
        return;

      async Task ShowDialog()
      {
        var dialogRes = await Forge.Forms.Show.Window().For(new Prompt<bool>
        {
          Title   = "Warning",
          Message = "There are unsaved changes. If you confirm, you will lose them."
        }).ConfigureAwait(false);

        if (dialogRes.Model.Confirmed)
          Close();
      }

      await Dispatcher.Invoke(ShowDialog).ConfigureAwait(false);
    }

    private bool HasChanges()
    {
      return Models.Any(m => m is INotifyPropertyChangedEx npc && npc.IsChanged);
    }

    private void Window_Closed(object sender, EventArgs e)
    {
      Dispatcher.Invoke(() => SingletonSemaphore.Release());
    }

    /// <summary>Handle exception throw during ICommand execution</summary>
    /// <param name="ex"></param>
    private async Task HandleExceptionAsync(Exception ex)
    {
      LogTo.Error(ex, "Exception occured during a user requested operation in a Configuration window");

      await Dispatcher.Invoke(
        async () =>
        {
          var errMsg = $"An error occured: {ex.Message}";

          await errMsg.ErrorMsgBox().ConfigureAwait(false);
        }
      ).ConfigureAwait(false);
    }

    /// <summary>Instantiates a new <see cref="ConfigurationWindow" /> if none other exist</summary>
    /// <param name="configModels">The configuration class instances that should be displayed</param>
    /// <returns>New instance or <see langword="null" /></returns>
    public static ConfigurationWindow ShowAndActivate(params INotifyPropertyChanged[] configModels)
    {
      return ShowAndActivate(null, configModels);
    }

    /// <summary>Instantiates a new <see cref="ConfigurationWindow" /> if none other exist</summary>
    /// <param name="title">The configuration window's title</param>
    /// <param name="configModels">The configuration class instances that should be displayed</param>
    /// <returns>New instance or <see langword="null" /></returns>
    public static ConfigurationWindow ShowAndActivate(string title, params INotifyPropertyChanged[] configModels)
    {
      return ShowAndActivate(null, null, configModels);
    }

    /// <summary>Instantiates a new <see cref="ConfigurationWindow" /> if none other exist</summary>
    /// <param name="title">The configuration window's title</param>
    /// <param name="hotKeyManager">
    ///   An optional instance of a <see cref="HotKeyManager" /> that will be used to provide hotkey rebinding options to the
    ///   user
    /// </param>
    /// <param name="configModels">The configuration class instances that should be displayed</param>
    /// <returns>New instance or <see langword="null" /></returns>
    public static ConfigurationWindow ShowAndActivate(string                          title,
                                                      HotKeyManager                   hotKeyManager,
                                                      params INotifyPropertyChanged[] configModels)
    {
      return Application.Current.Dispatcher.Invoke(() =>
      {
        if (SingletonSemaphore.WaitOne(0) == false)
          return null;

        var cfgWdw = new ConfigurationWindow(title, hotKeyManager, configModels);
        cfgWdw.ShowAndActivate();

        return cfgWdw;
      });
    }

    private MethodInfo GetMethodInfo(Type type, string name)
    {
      var typeMethods = TypeMethodsMap.SafeGet(type, new Dictionary<string, MethodInfo>());
      var method      = typeMethods.SafeGet(name);

      if (method == null)
      {
        method = type.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance);

        if (method == null)
          throw new InvalidOperationException($"Method {name} of type {type.FullName} couldn't be found");

        typeMethods[name] = method;
      }

      return method;
    }

    private object MapClone(object original)
    {
      return GetMethodInfo(original.GetType(), nameof(CfgBase<object>.MapClone)).Invoke(original, null);
    }

    private object ApplyChanges(object original, object model)
    {
      return GetMethodInfo(model.GetType(), nameof(CfgBase<object>.ApplyChanges)).Invoke(model, new[] { original });
    }

    #endregion




    #region Events

    /// <inheritdoc />
    public event PropertyChangedEventHandler PropertyChanged;

    #endregion
  }
}

using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using System;

namespace ScoopCmdPaletteExtension;

internal sealed partial class MainPage
{
    internal partial class InstallCommand : InvokableCommand
    {
        private readonly Scoop _scoop;
        private readonly ScoopSearchResultItem _package;
        private readonly ProgressState _progressState = new() { IsIndeterminate = false, ProgressPercent = 10 };
        private readonly ToastStatusMessage _toast;
        public InstallCommand(Scoop scoop, ScoopSearchResultItem package)
        {
            Name = Properties.Resources.InstallCommand;
            Icon = new("\uEBD3");
            _scoop = scoop;
            _package = package;
            _toast = new(new StatusMessage
            {
                Message = Properties.Resources.InstallProgressCheck,
                State = MessageState.Info,
                Progress = _progressState,
            })
            {
                Duration = -1,
            };
        }

        public override ICommandResult Invoke()
        {

            _toast.Show();
            string pkg = _package.Name;
            string repository = _package.Metadata.Repository;
            string filePath = _package.Metadata.FilePath;
            string fullPath = new Uri(new Uri(repository), filePath).ToString();
            try
            {
                ScoopBucket? bucket = _scoop.GetInstalledBucketFromSourceAsync(repository).GetAwaiter().GetResult();
                _progressState.ProgressPercent = 25;
                _toast.Message.Message = Properties.Resources.InstallProgressUpdate;
                Scoop.UpdateAsync().Wait();
                _progressState.ProgressPercent = 50;

                if (bucket != null)
                {
                    DoInstall($"{bucket.Name}/{pkg}");
                    return CommandResult.KeepOpen();
                }

                string bucketName = _scoop.GetBucketNameFromRepoAsync(repository).GetAwaiter().GetResult();
                return CommandResult.Confirm(new ConfirmationArgs
                {
                    Title = $"Bucket \"{bucketName}\" is not installed.",
                    Description = $"Do you want to install it from the repository \"{repository}\"?",
                    PrimaryCommand = new AnonymousCommand(() =>
                    {
                        _toast.Message.Message = $"Installing bucket \"{bucketName}\" from repository \"{repository}\"...";
                        _toast.Show();
                        try
                        {
                            _scoop.InstallBucketAsync(repository, bucketName).Wait();
                            _progressState.ProgressPercent = 75;
                            DoInstall($"{bucketName}/{pkg}");
                        }
                        catch (Exception ex)
                        {
                            _toast.Message.State = MessageState.Error;
                            _toast.Message.Message = ex.Message;
                            _toast.Show();
                        }
                    })
                    {
                        Name = "Install Bucket",
                        Result = CommandResult.KeepOpen()
                    },
                });

            }
            catch (Exception ex)
            {
                _toast.Message.State = MessageState.Error;
                _toast.Message.Message = ex.Message;
                _toast.Show();
                return CommandResult.KeepOpen();
            }
        }

        private void DoInstall(string packageName)
        {
            _toast.Message.Message = $"Installing package \"{packageName}\"...";
            _toast.Message.State = MessageState.Info;
            _toast.Message.Progress = new ProgressState { IsIndeterminate = true };
            _toast.Show();
            try
            {
                Scoop.InstallAsync(packageName).Wait();
                _toast.Message.Message = $"Package \"{packageName}\" installed successfully.";
                _toast.Message.State = MessageState.Success;
                _toast.Message.Progress = new ProgressState { IsIndeterminate = false, ProgressPercent = 100 };
                _toast.Show();
            }
            catch (Exception ex)
            {
                _toast.Message.State = MessageState.Error;
                _toast.Message.Message = $"Error installing package \"{packageName}\": {ex.Message}";
                _toast.Show();
                return;
            }
        }
    }
}

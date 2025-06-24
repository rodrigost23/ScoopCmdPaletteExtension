using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using System;

namespace ScoopCmdPaletteExtension;

internal sealed partial class ScoopCmdPaletteExtensionPage
{
    internal partial class InstallCommand : InvokableCommand
    {
        private readonly Scoop _scoop;
        private readonly ScoopSearchResultItem _package;
        public InstallCommand(Scoop scoop, ScoopSearchResultItem package)
        {
            Name = "Install";
            Icon = new("\uEBD3");
            _scoop = scoop;
            _package = package;
        }

        public override ICommandResult Invoke()
        {
            ProgressState progressState = new() { IsIndeterminate = false, ProgressPercent = 10 };
            ToastStatusMessage toast = new(new StatusMessage
            {
                Message = $"Checking bucket before installation...",
                State = MessageState.Info,
                Progress = progressState,
            })
            {
                Duration = -1,
            };
            toast.Show();
            string pkg = _package.Name;
            string repository = _package.Metadata.Repository;
            string filePath = _package.Metadata.FilePath;
            string fullPath = new Uri(new Uri(repository), filePath).ToString();
            try
            {
                ScoopBucket? bucket = _scoop.GetInstalledBucketFromSource(repository);
                progressState.ProgressPercent = 25;
                toast.Message.Message = $"Updating Scoop...";
                _scoop.UpdateAsync().Wait();
                progressState.ProgressPercent = 50;

                if (bucket != null)
                {
                    DoInstall(toast, $"{bucket.Name}/{pkg}");
                    return CommandResult.KeepOpen();
                }

                string bucketName = _scoop.GetBucketNameFromRepoAsync(repository).GetAwaiter().GetResult();
                return CommandResult.Confirm(new ConfirmationArgs
                {
                    Title = $"Bucket \"{bucketName}\" is not installed.",
                    Description = $"Do you want to install it from the repository \"{repository}\"?",
                    PrimaryCommand = new AnonymousCommand(() =>
                    {
                        toast.Message.Message = $"Installing bucket \"{bucketName}\" from repository \"{repository}\"...";
                        toast.Show();
                        try
                        {
                            _scoop.InstallBucketAsync(repository, bucketName).Wait();
                            progressState.ProgressPercent = 75;
                            DoInstall(toast, $"{bucketName}/{pkg}");
                        }
                        catch (Exception ex)
                        {
                            toast.Message.State = MessageState.Error;
                            toast.Message.Message = ex.Message;
                            toast.Show();
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
                toast.Message.State = MessageState.Error;
                toast.Message.Message = ex.Message;
                toast.Show();
                return CommandResult.KeepOpen();
            }
        }

        private void DoInstall(ToastStatusMessage toast, string packageName)
        {
            toast.Message.Message = $"Installing package \"{packageName}\"...";
            toast.Message.State = MessageState.Info;
            toast.Message.Progress = new ProgressState { IsIndeterminate = true };
            toast.Show();
            try
            {
                _scoop.InstallAsync(packageName).Wait();
                toast.Message.Message = $"Package \"{packageName}\" installed successfully.";
                toast.Message.State = MessageState.Success;
                toast.Message.Progress = new ProgressState { IsIndeterminate = false, ProgressPercent = 100 };
                toast.Show();
            }
            catch (Exception ex)
            {
                toast.Message.State = MessageState.Error;
                toast.Message.Message = $"Error installing package \"{packageName}\": {ex.Message}";
                toast.Show();
                return;
            }
        }
    }
}

using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Horizon.Game.GengDi.Core.ViewModels
{
    /// <summary>
    /// 支持取消后台任务的 ViewModel 接口。
    /// 页面切换时，MainViewModel 会调用 Cancel 以终止前一页面未完成的后台任务，
    /// 避免后台任务在 UI 线程被占用时排队等待而导致死锁。
    /// </summary>
    public interface ICancelableViewModel
    {
        /// <summary>
        /// 取消所有未完成的后台任务（HTTP 请求、UI 线程调度等）。
        /// 实现应保证该方法非阻塞且线程安全。
        /// </summary>
        void Cancel();
    }

    public class ViewModelBase : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
            {
                return false;
            }

            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }
}
using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace Horizon.Core
{
    /// <summary>
    /// 网络连接辅助类
    /// </summary>
    public static class NetHelper
    {
        /// <summary>
        /// 通信连接是否可用
        /// </summary>
        public static bool CanConnect { get; set; }

        /// <summary>
        /// 通信连接是否保持畅通
        /// </summary>
        public static bool IsConnected { get; set; }

        /// <summary>
        /// 获取网络延迟时间,单位毫秒
        /// </summary>
        /// <param name="hostOrIPStr">Host址或IP地址</param>
        /// <returns>返回网络延迟, -1表示网络不通</returns>
        public static int GetDey(string hostOrIPStr)
        {
            //构造Ping实例
            Ping pingSender = new Ping();
            //Ping 选项设置
            PingOptions options = new PingOptions();
            options.DontFragment = true;
            //测试数据
            string data = "";
            byte[] buffer = Encoding.ASCII.GetBytes(data);
            //设置超时时间
            int timeout = 120;
            //调用同步 send 方法发送数据,将返回结果保存至PingReply实例
            PingReply reply = pingSender.Send(hostOrIPStr, timeout, buffer, options);
            int delayTime = 0;
            if (reply.Status == IPStatus.Success)
            {
                delayTime = (int)reply.RoundtripTime;
                CanConnect = true;
            }
            else
            {
                delayTime = -1;
                CanConnect = false;
            }
            return delayTime;
        }


        /// <summary>
        /// 扫描地址内的端口是否能通信
        /// </summary>
        /// <param name="hostAddressOrIp"></param>
        /// <param name="ports"></param>
        public static void ScanPort(string hostAddressOrIp, int[] ports)
        {
            try
            {
                IPAddress.TryParse(hostAddressOrIp, out IPAddress address);
                int count = ports.Length;
                AutoResetEvent[] arEvents = new AutoResetEvent[count]; // 同步对象
                for (int i = 0; i < count; i++)
                {
                    arEvents[i] = new AutoResetEvent(false); // 同步对象, 初始未触发
                    Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                    socket.Bind(new IPEndPoint(IPAddress.Any, 0));
                    socket.BeginConnect(new IPEndPoint(address, ports[i]),
                    PingCallback,
                    new ArrayList() { socket, ports[i], arEvents[i] } // 传递3个参数: 当前socket, 端口, 同步对象
                    );
                }
                WaitHandle.WaitAll(arEvents); // 等待所有扫描工作完成
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }
        private static void PingCallback(IAsyncResult ar) // 扫描完成后的回调方法
        {
            ArrayList list = (ArrayList)ar.AsyncState; // 获取传递的参数
            Socket socket = (Socket)list[0];
            int port = (int)list[1];
            AutoResetEvent arevent = (AutoResetEvent)list[2];
            if (ar.IsCompleted && socket.Connected)
                IsConnected = true; // 检查 connected属性, 如果为true 则表示是开放的
            else
                IsConnected = false;
            try
            {
                socket.Shutdown(SocketShutdown.Both);
                socket.Close();
            }
            catch
            {
            }
            arevent.Set(); // 完成后触发
        }
    }
}

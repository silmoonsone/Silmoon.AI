using System;

namespace Silmoon.AI.Prompts
{
    public class UtilPrompt
    {
        public static string ContextPrompt { get; set; } = $"""
            ## 当前你运行的环境信息 ##

            - 当前系统登录用户: {Environment.UserName}
            - 当前操作系统: {Environment.OSVersion.VersionString}
            - 当前系统平台: {Environment.OSVersion.Platform}
            - 当前执行程序完整路径: {Environment.ProcessPath}
            - 当前工作目录: {Environment.CurrentDirectory}
            - 当前用户主目录: {Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)}
            """;
    }
}

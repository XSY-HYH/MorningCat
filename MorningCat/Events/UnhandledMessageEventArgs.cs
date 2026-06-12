using System;
using MorningCat.MDC;
using MorningCat.PlatformAbstraction;

namespace MorningCat.Events
{
    public class UnhandledMessageEventArgs : EventArgs
    {
        public PlatformMessage Message { get; }
        
        public string UserId => Message.SenderId;
        
        public string UserNickname => Message.SenderDisplayName;
        
        public string? GroupId => Message.GroupId;
        
        public string GroupName => Message.GroupName ?? Message.GroupId ?? "未知群";
        
        public string PlainText => Message.PlainText ?? "";
        
        public bool IsGroupMessage => Message.MessageType == UnifiedMessageType.Group;
        
        public bool IsPrivateMessage => Message.MessageType == UnifiedMessageType.Private;
        
        public UnhandledMessageEventArgs(PlatformMessage message)
        {
            Message = message;
        }
    }
}

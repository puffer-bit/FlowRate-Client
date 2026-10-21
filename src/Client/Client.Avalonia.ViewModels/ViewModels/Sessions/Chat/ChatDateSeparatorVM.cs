using Client.Avalonia.Localization;
using System;

namespace Client.ViewModels.Sessions.Chat;

public class ChatDateSeparatorVM : IMessageListItemVM
{
    public string DateText { get; set; }
    
    public ChatDateSeparatorVM(DateTime dateTime)
    {
        DateTime localDate = dateTime.Kind == DateTimeKind.Utc 
            ? dateTime.ToLocalTime().Date 
            : dateTime.Date;

        DateTime today = DateTime.Today;

        if (localDate == today)
        {
            DateText = Strings.Get("date.today");
        }
        else if (localDate == today.AddDays(-1))
        {
            DateText = Strings.Get("date.yesterday");
        }
        else
        {
            DateText = Strings.FormatLongDate(localDate);
        }
    }

}
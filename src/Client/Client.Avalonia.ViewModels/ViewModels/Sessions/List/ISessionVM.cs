using System.Collections.Generic;
using Client.Core.Sessions;


namespace Client.ViewModels.Sessions.List;

public interface ISessionVM : ISessionListRow
{
    /// <inheritdoc cref="ISessionListRow.IsFolded"/>
    new bool IsFolded { get; set; }

    // UI
    bool IsEntered { get; set; }

    /// <summary>
    /// The session is the one the main window shows now; the list highlights it.
    /// </summary>
    bool IsShown { get; set; }

    /// <summary>
    /// The session is playing its leave animation and is removed from the list right after it.
    /// </summary>
    bool IsLeaving { get; set; }
    
    /// <summary>
    /// The places the menu of the session offers to move it to, shared by all sessions of the list.
    /// </summary>
    IReadOnlyList<MoveTargetVM> MoveTargets { get; set; }

    // Session data
    SessionModel Model { get; set; }
}
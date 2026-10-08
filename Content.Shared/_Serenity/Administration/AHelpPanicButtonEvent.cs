using Robust.Shared.Serialization;

namespace Content.Shared._Serenity.Administration;

/// <summary>
/// Sent by a player who pressed and confirmed the panic button in their ahelp window.
/// The server turns it into a flagged ahelp message and an alert in admin chat.
/// </summary>
[Serializable, NetSerializable]
public sealed class AHelpPanicButtonEvent : EntityEventArgs
{
}

using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._Serenity.Medical.Sterility;

[Serializable, NetSerializable]
public sealed partial class PathogenAnalyzeDoAfterEvent : SimpleDoAfterEvent;

[Serializable, NetSerializable]
public sealed partial class PathogenSynthesizeDoAfterEvent : SimpleDoAfterEvent;

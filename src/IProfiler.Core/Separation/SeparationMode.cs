namespace IProfiler.Core.Separation;

/// <summary>How a stream of pages is split into documents. Replaces the legacy untyped int flag.</summary>
public enum SeparationMode
{
    PageCount,
    Barcode,
    BlankPage,
    PatchCode,
    KeywordZone,
}

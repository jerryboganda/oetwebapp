"""One-shot patch: insert ConfigureHttpJsonOptions(IgnoreCycles) into Program.cs.

Run on VPS only: python3 /tmp/patch_program.py
"""
import pathlib
import sys

p = pathlib.Path("/opt/oetwebapp/backend/src/OetLearner.Api/Program.cs")
src = p.read_text()
if "ConfigureHttpJsonOptions" in src:
    print("ALREADY_PATCHED")
    sys.exit(0)
anchor = "builder.Services.AddProblemDetails();"
patch = """

// Minimal-API JSON: ignore reference cycles so accidental EF-entity
// returns (e.g. Paper -> Assets -> Paper) do not 400/500 after the
// SaveChangesAsync commit. Endpoints should still prefer flat DTOs.
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.ReferenceHandler =
        System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
});"""
if src.count(anchor) != 1:
    print(f"ANCHOR_COUNT={src.count(anchor)}")
    sys.exit(1)
p.write_text(src.replace(anchor, anchor + patch))
print("PATCHED")

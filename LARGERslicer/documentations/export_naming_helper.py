"""
Export naming helper for GH Python workflows.

Naming schema:
PSYYYYMMDD_Project - DocumentType - PartNumber - SubPart - Revision

Revision is optional and omitted when empty.
"""

import datetime
import System


def collapse_whitespace(text):
    if text is None:
        return ""
    return " ".join(str(text).split())


def sanitize_token(value, fallback=""):
    raw = fallback if value is None or str(value).strip() == "" else str(value).strip()
    invalid = set(System.IO.Path.GetInvalidFileNameChars())

    chars = []
    for c in raw:
        if c in invalid:
            chars.append("_")
        else:
            chars.append(c)

    return collapse_whitespace("".join(chars).strip())


def ensure_extension(ext, default_ext=".txt"):
    if ext is None:
        return default_ext
    value = str(ext).strip()
    if value == "":
        return default_ext
    return value if value.startswith(".") else "." + value


def build_base_name(
    project_name,
    part_number,
    sub_part,
    revision="",
    document_type="Geometrie",
    date_value=None,
):
    if date_value is None:
        date_value = datetime.datetime.now()

    prefix = "PS" + date_value.strftime("%Y%m%d")
    project = sanitize_token(project_name, "Projekt")
    part = sanitize_token(part_number, "Teil")
    sub = sanitize_token(sub_part, "Allgemein")
    doc = sanitize_token(document_type, "Geometrie")
    rev = sanitize_token(revision, "")

    base = "{}_{:s} - {:s} - {:s} - {:s}".format(prefix, project, doc, part, sub)
    if rev:
        base += " - " + rev
    return collapse_whitespace(base.strip())

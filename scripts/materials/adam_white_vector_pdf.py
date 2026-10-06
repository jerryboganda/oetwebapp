#!/usr/bin/env python3
"""Vector (real text) retype of the Nursing writing case notes "Adam White".

Why this exists (owner order 6-7 Oct 2026): the live Nursing Free Sample is a raster PDF cut from a 960x540 JPEG,
so it is soft at every zoom. The owner approved a verbatim vector retype of the SAME document: same wording, same
two-page split (page 1 = left half of the original, page 2 = right half), sharp at any zoom.

Wording is transcribed from OET/Materials ( To be Uploaded )/Writing/Nursing/Nursing Case Notes/Adam White.jpg by
three independent transcribers, reconciled, then proofread twice against the image. Source slips are kept as printed
(see KEPT_AS_PRINTED), nothing is corrected or reworded. All compute runs in GitHub Actions
(.github/workflows/sample-pdf.yml), never on a workstation or the VPS.

Usage: adam_white_vector_pdf.py OUTPUT.pdf
"""
import os
import sys

from reportlab.lib.colors import black, white
from reportlab.lib.pagesizes import A4
from reportlab.lib.styles import ParagraphStyle
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont
from reportlab.platypus import PageBreak, Paragraph, SimpleDocTemplate, Spacer, Table, TableStyle

# Printed in the source image (kept verbatim, listed so the owner can review them):
KEPT_AS_PRINTED = [
    "anaesthetc (Nursing Management bullet: 'not full anaesthetc')",
    "intc (Writing task: 'Expand the relevant notes intc complete sentences')",
    "Surgicentre,100 Plain Road (no space after the comma)",
]


def find_font(*candidates):
    for path in candidates:
        if os.path.exists(path):
            return path
    sys.exit("font not found: " + " | ".join(candidates))


pdfmetrics.registerFont(TTFont("Body", find_font(
    "/usr/share/fonts/truetype/liberation/LiberationSans-Regular.ttf",
    "/usr/share/fonts/truetype/liberation2/LiberationSans-Regular.ttf")))
pdfmetrics.registerFont(TTFont("Body-Bold", find_font(
    "/usr/share/fonts/truetype/liberation/LiberationSans-Bold.ttf",
    "/usr/share/fonts/truetype/liberation2/LiberationSans-Bold.ttf")))
pdfmetrics.registerFont(TTFont("Sym", find_font("/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf")))
pdfmetrics.registerFontFamily("Body", normal="Body", bold="Body-Bold", italic="Body", boldItalic="Body-Bold")

TICK = '<font name="Sym">&#10004;</font>'    # heavy check mark
ARROW = '<font name="Sym">&#8594;</font>'    # rightwards arrow
RING = '<font name="Sym">&#730;</font>'      # ring above (printed as "37˚C")

BASE = ParagraphStyle("base", fontName="Body", fontSize=10.5, leading=14.5, textColor=black)
BOLD = ParagraphStyle("bold", parent=BASE, fontName="Body-Bold")
HEAD = ParagraphStyle("head", parent=BASE, fontName="Body-Bold", fontSize=12, leading=16)
TITLE = ParagraphStyle("title", parent=BASE, fontName="Body-Bold", fontSize=21, leading=26)
BOXED = ParagraphStyle("boxed", parent=BASE, fontName="Body-Bold", textColor=white)
SMALL = ParagraphStyle("small", parent=BASE, fontSize=9.5, leading=13)

CONTENT_W = A4[0] - 84        # 42 pt side margins
LABEL_W = 118
VALUE_W = CONTENT_W - LABEL_W


def p(text, style=BASE):
    return Paragraph(text, style)


def box(label, width):
    """White bold text on a black filled box ("Notes:", "Writing Task:")."""
    t = Table([[p(label, BOXED)]], colWidths=[width], hAlign="LEFT")
    t.setStyle(TableStyle([
        ("BACKGROUND", (0, 0), (-1, -1), black),
        ("LEFTPADDING", (0, 0), (-1, -1), 4), ("RIGHTPADDING", (0, 0), (-1, -1), 4),
        ("TOPPADDING", (0, 0), (-1, -1), 2), ("BOTTOMPADDING", (0, 0), (-1, -1), 3),
    ]))
    return t


def plain_table(rows, widths, pad_bottom=5, hAlign="LEFT"):
    t = Table(rows, colWidths=widths, hAlign=hAlign)
    t.setStyle(TableStyle([
        ("VALIGN", (0, 0), (-1, -1), "TOP"),
        ("LEFTPADDING", (0, 0), (-1, -1), 0), ("RIGHTPADDING", (0, 0), (-1, -1), 0),
        ("TOPPADDING", (0, 0), (-1, -1), 0), ("BOTTOMPADDING", (0, 0), (-1, -1), pad_bottom),
    ]))
    return t


def stack(lines, style=BASE):
    """Separate lines of one value cell, each its own paragraph."""
    return [p(line, style) for line in lines]


def row(label, value, style=BASE):
    return [p("<b>%s</b>" % label), value if isinstance(value, list) else p(value, style)]


def bullets(items, style=BASE, width=VALUE_W):
    """Round-bullet list whose wrapped lines align under the text."""
    return plain_table([[p("&#8226;", style), p(text, style)] for text in items], [14, width - 14], pad_bottom=3)


def page_one():
    s = []
    s.append(p("OCCUPATIONAL ENGLISH TEST", TITLE))
    s.append(Spacer(1, 14))
    s.append(plain_table([
        [p("WRITING SUB-TEST:", HEAD), p("NURSING", HEAD), ""],
        [p("TIME ALLOWED:", HEAD), p("READING TIME:", HEAD), p("5 MINUTES", HEAD)],
        ["", p("WRITING TIME:", HEAD), p("40 MINUTES", HEAD)],
    ], [150, 130, 120], pad_bottom=6))
    s.append(Spacer(1, 6))
    s.append(p("Read the case notes below and complete the writing task which follows.", SMALL))
    s.append(Spacer(1, 12))
    s.append(box("Notes:", 52))
    s.append(Spacer(1, 6))
    s.append(p("You are a practice nurse for a general practitioner and you are preparing a patient for a day "
               "colonoscopy procedure in hospital next week. Your practice is seeing the patient for the first time."))
    s.append(Spacer(1, 8))

    history = plain_table([
        [p("<b>2014</b>"), p("Bowel issues &#8211; constipation ?due to poor diet.")],
        [p("<b>2015</b>"), p("PSA, FBE &#8211; all %s, discussed diet, not overweight, Pt sees no reason to worry." % TICK)],
        [p("<b>2016</b>"), p("Very bad cold, ?flu. Viral &#8211; given usual flu advice, no antibiotics.")],
    ], [34, VALUE_W - 34], pad_bottom=3)

    rows = [
        row("Patient:", "Mr Adam White (DOB: 25 July, 1958)"),
        row("Address:", "20 Leonard St, Lakeside"),
        row("Social Background:", stack([
            "Single. Lives alone. IT consultant. Eats out a lot; entertains; heavy drinker (alcohol); rarely smokes"])),
    ]
    s.append(plain_table(rows, [LABEL_W, VALUE_W]))
    s.append(Spacer(1, 2))
    s.append(p("<b><u>1 August 2018</u></b>"))
    s.append(Spacer(1, 3))
    rows = [
        row("Reason for presenting:", ""),
        ["", p("Results of bowel cancer screen test &#8211; positive. Pt wants a colonoscopy.")],
        row("Medications:", "Occasional paracetamol, rare ibuprofen (for pain in L leg)."),
        row("Allergies:", "No known allergies (NKA), sensitive to codeine."),
        row("Medical History:", [history]),
        row("Family History:", stack([
            "Grandfather (paternal): ?cancer bowel, ?had colostomy.",
            "Father: Cancer colon (1997), recent resection, anastomosis, no problems since."])),
        row("Dr Consultation:", stack([
            "Pt complaining of bowel changes &amp; notification of bowel tests &#8211; abnormal.",
            "Very worried due to family history.",
            "Colonoscopy in 1 wk. Explained procedure. Bowel prep prescribed.",
            "Pathology ordered.",
            "Recommended &#8211; discussions with nurse re pre-procedure assessment &amp; management.",
            "R/V Dr &amp; Nurse 1 wk post-procedure."])),
        row("Nurse Discussions:", stack([
            "Explained pre-procedure assessment and management.",
            "Letter to day surgery nurse at clinic where procedure will take place."])),
    ]
    s.append(plain_table(rows, [LABEL_W, VALUE_W], pad_bottom=7))
    return s


def page_two():
    s = []
    management = bullets([
        "Observations &#8211; T: 37%sC, P: 88, BP: 145/95 (notified Dr), R: 16" % RING,
        "Pathology &#8211; FBE, U&amp;Es, LFTs (will receive results pre-procedure)",
        "Wt: 80kg, Ht: 190cm",
        "Immediate cessation of smoking and reduction of alcohol intake advised",
        "Previous anaesthetics &#8211; no issues %s Explained will be heavily sedated, not full anaesthetc" % ARROW,
        "NKA. Sensitive to codeine. Alert sticker attached to paperwork<br/>"
        "%s Advised to notify all staff, but unlikely to need that amount of analgesic" % ARROW,
        "Explained procedure &#8211; pre &amp; post expectations to patient",
        "Pt signed endoscopy (colonoscopy) consent form",
        "Booklet given &#8211; procedure, risks, pain, bleeding, etc.",
        "Pre-procedure:&nbsp; Day before &#8211; light breakfast then clear fluids only<br/>"
        "Bowel prep instructions &#8211; fast midnight before procedure",
        "Admission booklet explained %s Pt arranged hospital pickup" % ARROW,
        "Discussed importance of healthy diet &amp; exercise",
    ])
    plan = bullets([
        "R/V 1 wk post-procedure Dr &amp; Nurse",
        "R/V progress, further counselling, other options if necessary. Results of colonoscopy will determine "
        "course of action.",
    ])
    s.append(plain_table([
        [p("<b>Nursing Management:</b>"), management],
    ], [LABEL_W, VALUE_W], pad_bottom=10))
    s.append(plain_table([
        [Paragraph("<b>Plan:</b>", ParagraphStyle("plan", parent=BASE, leftIndent=26)), plan],
    ], [LABEL_W, VALUE_W], pad_bottom=10))
    s.append(Spacer(1, 10))
    s.append(box("Writing Task:", 84))
    s.append(Spacer(1, 8))
    s.append(p("Using the information given in the case notes, write a letter to the Day Surgery Nurse at "
               "Surgicentre,100 Plain Road, Lakeside, outlining the patient's relevant history and current "
               "circumstances, including your pre-procedure assessment and management."))
    s.append(Spacer(1, 10))
    s.append(p("<b>In your answer:</b>"))
    s.append(Spacer(1, 3))
    s.append(plain_table([[bullets([
        "<b>Expand the relevant notes intc complete sentences</b>",
        "<b>Do <u>not</u> use note form</b>",
        "<b>Use letter format</b>",
    ], width=CONTENT_W - 20)]], [CONTENT_W], pad_bottom=2))
    s.append(Spacer(1, 3))
    s.append(p("<b>The body of the letter should be approximately 180&#8211;200 words.</b>"))
    return s


def main():
    if len(sys.argv) != 2:
        sys.exit(__doc__)
    out = sys.argv[1]
    doc = SimpleDocTemplate(
        out, pagesize=A4, leftMargin=42, rightMargin=42, topMargin=46, bottomMargin=46,
        title="OET Writing - Nursing - Adam White (case notes)", author="", subject="",
    )
    story = page_one() + [PageBreak()] + page_two()
    doc.build(story)
    print("wrote %s (%d bytes); kept as printed: %s" % (out, os.path.getsize(out), "; ".join(KEPT_AS_PRINTED)))


if __name__ == "__main__":
    main()

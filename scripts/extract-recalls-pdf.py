import sys, pathlib
import pdfplumber

pdf_path = r"C:\Users\Dr Faisal Maqsood PC\Downloads\RECENT UPDATED LISTENING Recalls From JANUARY 2023 Till The End of 2025.pdf"
out = pathlib.Path(r"C:\Users\Dr Faisal Maqsood PC\Desktop\New OET Web App\_extracted\recalls-2023-2025.txt")
out.parent.mkdir(parents=True, exist_ok=True)

parts = []
with pdfplumber.open(pdf_path) as pdf:
    for i, page in enumerate(pdf.pages, 1):
        parts.append(f"\n----- PAGE {i} -----\n" + (page.extract_text() or ""))

text = "\n".join(parts)
out.write_text(text, encoding="utf-8")
print(f"WROTE {len(text)} chars, {len(parts)} pages -> {out}")

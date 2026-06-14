import fitz, os

pdfs = {
    "rulebook": r"C:\Users\Dr Faisal Maqsood PC\Downloads\OET Reading Rulebook for Both Paper & Computer Based ( IMPORTANT NOTE-THE SAME FOR ALL PROFESSIONS ).pdf",
    "qp_partA": r"C:\Users\Dr Faisal Maqsood PC\Desktop\OET with Dr. Ahmed Hesham ( Medicine Only )\Reading ( IMPORTANT NOTE = Same for All Professions )\Reading Sample 1\Question Paper ( Part A Reading ).pdf",
    "text_partA": r"C:\Users\Dr Faisal Maqsood PC\Desktop\OET with Dr. Ahmed Hesham ( Medicine Only )\Reading ( IMPORTANT NOTE = Same for All Professions )\Reading Sample 1\Text Booklet ( Part A Reading ).pdf",
    "ans_partA": r"C:\Users\Dr Faisal Maqsood PC\Desktop\OET with Dr. Ahmed Hesham ( Medicine Only )\Reading ( IMPORTANT NOTE = Same for All Professions )\Reading Sample 1\Answers ( Part A Reading ).pdf",
}

OUT = r"D:\Projects\NEW OET WEB APP\.tmp_render\txt"
os.makedirs(OUT, exist_ok=True)

for key, path in pdfs.items():
    doc = fitz.open(path)
    chars = 0
    parts = []
    for i in range(len(doc)):
        t = doc[i].get_text()
        chars += len(t.strip())
        parts.append(f"===== {key} PAGE {i+1} =====\n{t}")
    with open(os.path.join(OUT, key + ".txt"), "w", encoding="utf-8") as f:
        f.write("\n\n".join(parts))
    print(f"{key}: {len(doc)} pages, {chars} non-space chars extracted -> {'DIGITAL TEXT' if chars > 200 else 'LIKELY IMAGE/SCAN'}")
    doc.close()

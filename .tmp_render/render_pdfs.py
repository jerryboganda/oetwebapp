import fitz, os

OUT = r"D:\Projects\NEW OET WEB APP\.tmp_render\img"
os.makedirs(OUT, exist_ok=True)

pdfs = {
    "qp_partA": r"C:\Users\Dr Faisal Maqsood PC\Desktop\OET with Dr. Ahmed Hesham ( Medicine Only )\Reading ( IMPORTANT NOTE = Same for All Professions )\Reading Sample 1\Question Paper ( Part A Reading ).pdf",
    "text_partA": r"C:\Users\Dr Faisal Maqsood PC\Desktop\OET with Dr. Ahmed Hesham ( Medicine Only )\Reading ( IMPORTANT NOTE = Same for All Professions )\Reading Sample 1\Text Booklet ( Part A Reading ).pdf",
    "ans_partA": r"C:\Users\Dr Faisal Maqsood PC\Desktop\OET with Dr. Ahmed Hesham ( Medicine Only )\Reading ( IMPORTANT NOTE = Same for All Professions )\Reading Sample 1\Answers ( Part A Reading ).pdf",
    "partBC": r"C:\Users\Dr Faisal Maqsood PC\Desktop\OET with Dr. Ahmed Hesham ( Medicine Only )\Reading ( IMPORTANT NOTE = Same for All Professions )\Reading Sample 1\Reading Part B&C.pdf",
    "rulebook": r"C:\Users\Dr Faisal Maqsood PC\Downloads\OET Reading Rulebook for Both Paper & Computer Based ( IMPORTANT NOTE-THE SAME FOR ALL PROFESSIONS ).pdf",
}

zoom = 2.0  # ~144 DPI
mat = fitz.Matrix(zoom, zoom)

summary = []
for key, path in pdfs.items():
    if not os.path.exists(path):
        summary.append(f"{key}: MISSING -> {path}")
        continue
    doc = fitz.open(path)
    n = len(doc)
    for i in range(n):
        page = doc[i]
        pix = page.get_pixmap(matrix=mat)
        fn = os.path.join(OUT, f"{key}_p{i+1:02d}.png")
        pix.save(fn)
    summary.append(f"{key}: {n} pages rendered")
    doc.close()

print("\n".join(summary))

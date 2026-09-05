import os
import sys
import json
import subprocess
import zipfile
import xml.etree.ElementTree as ET
import fitz  # PyMuPDF

REPO_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))

def extract_docx(file_path):
    try:
        with zipfile.ZipFile(file_path) as z:
            xml_content = z.read("word/document.xml")
            tree = ET.fromstring(xml_content)
            paragraphs = []
            for p in tree.iter():
                if p.tag.endswith("}p"):
                    p_texts = [node.text for node in p.iter() if node.tag.endswith("}t") and node.text]
                    if p_texts:
                        paragraphs.append("".join(p_texts))
            if paragraphs:
                return "\n\n".join(paragraphs).strip()
            texts = [node.text for node in tree.iter() if node.tag.endswith("}t") and node.text]
            return "\n".join(texts).strip()
    except Exception as e:
        return f"[Error extracting docx: {e}]"

def is_page_scanned(page):
    """Detect if a PDF page is primarily an image/scan rather than extractable digital text."""
    raw_text = page.get_text().strip()
    clean = raw_text
    boilerplates = [
        "CamScanner",
        "Scanned with CamScanner",
        "Scanned by CamScanner",
        "Please record your answer on this page.",
        "(Only answers on Page 1 and Page 2 will be marked.)",
        "(Only answers on Page I and Page 2 will be marked.)",
        "(Only answers on Page 1 a Page 2 will be marked.)",
        "OET Writing sub-test – Answer booklet",
        "OET Writing sub-test - Answer booklet",
    ]
    for b in boilerplates:
        clean = clean.replace(b, "")
    clean = clean.strip()
    
    images = page.get_images()
    # If the clean digital text is sparse (< 60 chars) and the page contains image(s) or has 0 text
    if len(clean) < 60 and (len(images) > 0 or len(raw_text) == 0):
        return True
    return False

def run_batch_ocr(image_paths, temp_dir):
    if not image_paths:
        return {}
    os.makedirs(temp_dir, exist_ok=True)
    in_json = os.path.join(temp_dir, "batch_in.json")
    out_json = os.path.join(temp_dir, "batch_out.json")
    
    abs_paths = [os.path.abspath(p) for p in image_paths]
    with open(in_json, "w", encoding="utf-8") as f:
        json.dump(abs_paths, f)
        
    ocr_script = os.path.join(REPO_ROOT, "scripts", "batch-ocr.ps1")
    cmd = [
        "powershell",
        "-ExecutionPolicy", "Bypass",
        "-File", ocr_script,
        "-ImageListJson", in_json,
        "-OutputJson", out_json
    ]
    subprocess.run(cmd, check=True, cwd=REPO_ROOT)
    
    with open(out_json, "r", encoding="utf-8-sig") as f:
        data = json.load(f)
    return data

def extract_all(files, cache_path=None):
    temp_dir = os.path.join(REPO_ROOT, "artifacts", ".ocr_temp")
    os.makedirs(temp_dir, exist_ok=True)
    
    extracted = {}
    ocr_image_queue = []       # list of (key, abs_image_path)
    pdf_page_slots = {}        # rel -> list of (page_idx, 'digital'|'ocr', text_or_temp_path)
    all_pages_to_ocr = []      # list of temp_page_paths
    
    print(f"Analyzing {len(files)} files...")
    for f in files:
        rel = f.replace("\\", "/")
        abs_p = os.path.join(REPO_ROOT, rel)
        if not os.path.exists(abs_p):
            extracted[rel] = "[File not found]"
            continue
            
        ext = os.path.splitext(abs_p)[1].lower()
        if ext == ".docx":
            extracted[rel] = extract_docx(abs_p)
        elif ext == ".pdf":
            try:
                doc = fitz.open(abs_p)
                page_slots = []
                for p_idx, page in enumerate(doc):
                    if is_page_scanned(page):
                        pix = page.get_pixmap(dpi=200)
                        p_file = os.path.join(temp_dir, f"pdf_{len(all_pages_to_ocr)}_{p_idx}.png")
                        pix.save(p_file)
                        abs_p_file = os.path.abspath(p_file)
                        all_pages_to_ocr.append(abs_p_file)
                        page_slots.append((p_idx, 'ocr', abs_p_file))
                    else:
                        t = page.get_text().strip()
                        page_slots.append((p_idx, 'digital', t))
                pdf_page_slots[rel] = page_slots
            except Exception as e:
                extracted[rel] = f"[PDF open error: {e}]"
        elif ext in [".jpg", ".jpeg", ".png"]:
            ocr_image_queue.append((rel, os.path.abspath(abs_p)))
        else:
            extracted[rel] = "[Unsupported format]"

    # Run OCR on all queued images and rendered PDF pages
    total_to_ocr = [p for _, p in ocr_image_queue] + all_pages_to_ocr
    print(f"Running OCR on {len(total_to_ocr)} images/pages ({len(ocr_image_queue)} standalone, {len(all_pages_to_ocr)} scanned PDF pages)...")
    
    ocr_results = {}
    if total_to_ocr:
        ocr_results = run_batch_ocr(total_to_ocr, temp_dir)
        
        # Retry low-yield standalone images with upscaling & contrast boost
        retry_images = []
        for rel, abs_p in ocr_image_queue:
            txt = ocr_results.get(abs_p, "").strip()
            if len(txt) < 150:
                try:
                    from PIL import Image, ImageEnhance
                    img = Image.open(abs_p)
                    # 3x Lanczos scaling + contrast boost
                    img_scaled = img.convert('L').resize((img.width * 3, img.height * 3), Image.Resampling.LANCZOS)
                    enhancer = ImageEnhance.Contrast(img_scaled)
                    enhanced = enhancer.enhance(1.8)
                    retry_path = os.path.join(temp_dir, f"retry_{len(retry_images)}.png")
                    enhanced.save(retry_path)
                    retry_images.append((rel, abs_p, os.path.abspath(retry_path)))
                except Exception:
                    pass
                    
        if retry_images:
            retry_map = run_batch_ocr([p for _, _, p in retry_images], temp_dir)
            for rel, orig_abs, p in retry_images:
                new_txt = retry_map.get(p, "").strip()
                old_txt = ocr_results.get(orig_abs, "").strip()
                if len(new_txt) > len(old_txt):
                    ocr_results[orig_abs] = new_txt

        # Assemble standalone images
        for rel, abs_p in ocr_image_queue:
            extracted[rel] = ocr_results.get(abs_p, "").strip()

    # Assemble PDF files in original page order
    for rel, slots in pdf_page_slots.items():
        pages_text = []
        for p_idx, kind, val in slots:
            if kind == 'digital':
                if val:
                    pages_text.append(val)
            else:
                ocr_t = ocr_results.get(val, "").strip()
                if ocr_t:
                    pages_text.append(ocr_t)
        extracted[rel] = "\n\n--- Page Break ---\n\n".join(pages_text).strip()

    # Cleanup temp directory
    import shutil
    shutil.rmtree(temp_dir, ignore_errors=True)
    
    if cache_path:
        os.makedirs(os.path.dirname(os.path.abspath(cache_path)), exist_ok=True)
        with open(cache_path, "w", encoding="utf-8") as f:
            json.dump(extracted, f, ensure_ascii=False, indent=2)
        print(f"Cached {len(extracted)} extracted texts to {cache_path}")
        
    return extracted

if __name__ == "__main__":
    csv_path = os.path.join(REPO_ROOT, "artifacts", "developer-action-brief", "02-writing-qa-export.csv")
    import csv
    csv.field_size_limit(2147483647)
    with open(csv_path, mode="r", encoding="utf-8") as f:
        reader = csv.DictReader(f)
        files = [r["source_path"] for r in reader]
    cache = os.path.join(REPO_ROOT, "artifacts", "developer-action-brief", "writing-extracted-text.json")
    res = extract_all(files, cache)
    print("Done! Total files extracted:", len(res))
    empty_cnt = sum(1 for v in res.values() if not v or len(v.strip()) == 0)
    print(f"Empty results: {empty_cnt}/{len(res)}")

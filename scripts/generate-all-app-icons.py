import os
import struct
import io
from PIL import Image, ImageDraw

def main():
    print("Starting pixel-perfect app icon generation...")

    # Load master logo
    master_path = "public/brand/oet-square-logo.png"
    if not os.path.exists(master_path):
        raise FileNotFoundError(f"Master logo not found at {master_path}")
    
    logo = Image.open(master_path).convert("RGBA")
    
    # Crop to exact emblem bounding box
    alpha = logo.split()[3]
    bbox = alpha.getbbox()
    emblem = logo.crop(bbox)
    print(f"Emblem original crop size: {emblem.size}")

    def make_square_icon(emblem_img, size, padding_ratio=0.08, bg_color=None, is_round=False):
        if bg_color:
            canvas = Image.new("RGBA", (size, size), bg_color)
        else:
            canvas = Image.new("RGBA", (size, size), (0, 0, 0, 0))
        
        # Calculate available dimension for emblem
        target_dim = int(size * (1.0 - 2 * padding_ratio))
        ew, eh = emblem_img.size
        scale = target_dim / max(ew, eh)
        nw, nh = max(1, int(ew * scale)), max(1, int(eh * scale))
        
        resized = emblem_img.resize((nw, nh), Image.Resampling.LANCZOS)
        ox = (size - nw) // 2
        oy = (size - nh) // 2
        
        if is_round:
            # Create a rounded/circular mask
            mask = Image.new("L", (size, size), 0)
            draw = ImageDraw.Draw(mask)
            draw.ellipse((0, 0, size - 1, size - 1), fill=255)
            
            temp = Image.new("RGBA", (size, size), (0, 0, 0, 0))
            if not bg_color:
                # White or transparent circle background
                temp_bg = Image.new("RGBA", (size, size), (255, 255, 255, 255))
                temp.paste(temp_bg, (0,0))
            temp.alpha_composite(resized, (ox, oy))
            
            canvas.paste(temp, (0, 0), mask)
        else:
            canvas.alpha_composite(resized, (ox, oy))
            
        return canvas

    def save_multi_res_ico(emblem_img, output_path, sizes=[16, 24, 32, 48, 64, 128, 256]):
        png_data_list = []
        for s in sizes:
            ic = make_square_icon(emblem_img, s, padding_ratio=0.08)
            buf = io.BytesIO()
            ic.save(buf, format="PNG")
            png_data_list.append((s, s, buf.getvalue()))
        
        num_images = len(png_data_list)
        header = struct.pack("<HHH", 0, 1, num_images)
        offset = 6 + (num_images * 16)
        dir_entries = bytearray()
        image_bytes = bytearray()
        
        for w, h, data in png_data_list:
            bw = 0 if w >= 256 else w
            bh = 0 if h >= 256 else h
            size_data = len(data)
            entry = struct.pack("<BBBBHHII", bw, bh, 0, 0, 1, 32, size_data, offset)
            dir_entries.extend(entry)
            image_bytes.extend(data)
            offset += size_data
            
        with open(output_path, "wb") as f:
            f.write(header)
            f.write(dir_entries)
            f.write(image_bytes)
        print(f"Generated multi-resolution ICO at {output_path} ({num_images} sizes)")

    # 1. Generate Web / PWA / Next.js icons
    print("\n--- Generating Web & PWA Icons ---")
    make_square_icon(emblem, 512, padding_ratio=0.08).save("public/icon-512.png")
    make_square_icon(emblem, 192, padding_ratio=0.08).save("public/icon-192.png")
    make_square_icon(emblem, 512, padding_ratio=0.15, bg_color=(255, 255, 255, 255)).save("public/icon-maskable-512.png")
    make_square_icon(emblem, 32, padding_ratio=0.05).save("app/icon.png")
    make_square_icon(emblem, 180, padding_ratio=0.10, bg_color=(255, 255, 255, 255)).convert("RGB").save("app/apple-icon.png")
    save_multi_res_ico(emblem, "public/favicon.ico")

    # 2. Generate Desktop Icons (src-tauri/icons)
    print("\n--- Generating Desktop (Tauri) Icons ---")
    os.makedirs("src-tauri/icons", exist_ok=True)
    make_square_icon(emblem, 512, padding_ratio=0.08).save("src-tauri/icons/icon.png")
    make_square_icon(emblem, 128, padding_ratio=0.08).save("src-tauri/icons/128x128.png")
    make_square_icon(emblem, 256, padding_ratio=0.08).save("src-tauri/icons/128x128@2x.png")
    make_square_icon(emblem, 64, padding_ratio=0.08).save("src-tauri/icons/64x64.png")
    make_square_icon(emblem, 32, padding_ratio=0.08).save("src-tauri/icons/32x32.png")
    save_multi_res_ico(emblem, "src-tauri/icons/icon.ico")

    # 3. Generate Android Mobile Icons
    print("\n--- Generating Android Mobile Icons ---")
    android_densities = [
        ("mipmap-mdpi", 48, 108),
        ("mipmap-hdpi", 72, 162),
        ("mipmap-xhdpi", 96, 216),
        ("mipmap-xxhdpi", 144, 324),
        ("mipmap-xxxhdpi", 192, 432),
    ]

    for dir_name, legacy_sz, fg_sz in android_densities:
        target_dir = os.path.join("android/app/src/main/res", dir_name)
        os.makedirs(target_dir, exist_ok=True)
        
        # ic_launcher.png (legacy standard launcher icon, transparent background with emblem)
        make_square_icon(emblem, legacy_sz, padding_ratio=0.08).save(os.path.join(target_dir, "ic_launcher.png"))
        
        # ic_launcher_round.png (legacy round launcher icon)
        make_square_icon(emblem, legacy_sz, padding_ratio=0.10, is_round=True).save(os.path.join(target_dir, "ic_launcher_round.png"))
        
        # ic_launcher_foreground.png (Android Adaptive Icon foreground layer: 108dp canvas, safe zone 72dp -> padding 16.6%)
        make_square_icon(emblem, fg_sz, padding_ratio=0.20).save(os.path.join(target_dir, "ic_launcher_foreground.png"))
        
        print(f"Updated Android {dir_name}: legacy={legacy_sz}x{legacy_sz}, fg={fg_sz}x{fg_sz}")

    # 4. Generate iOS Mobile Icons
    print("\n--- Generating iOS Mobile Icons ---")
    ios_dir = "ios/App/App/Assets.xcassets/AppIcon.appiconset"
    os.makedirs(ios_dir, exist_ok=True)
    # iOS icons must be 100% opaque PNGs
    ios_app_icon = make_square_icon(emblem, 1024, padding_ratio=0.10, bg_color=(255, 255, 255, 255)).convert("RGB")
    ios_app_icon.save(os.path.join(ios_dir, "AppIcon-512@2x.png"))
    print(f"Updated iOS AppIcon-512@2x.png (1024x1024 opaque RGB)")

    print("\nAll desktop, mobile, and web icons successfully generated!")

if __name__ == "__main__":
    main()

import React, { useEffect, useState } from "react";
import "react-quill-new/dist/quill.snow.css";

const TextEditor2 = () => {
  const [ReactQuill, setReactQuill] = useState<any>(null);
  const [value, setValue] = useState("Hello!");

  const modules = {
    toolbar: [
      ["bold", "italic", "underline", "strike"],
      ["link", "image", "video"],
      ["clean"],
    ],
  };

  useEffect(() => {
    import("react-quill-new").then((mod) => {
      setReactQuill(() => mod.default);
    });
  }, []);

  if (!ReactQuill) return null;

  return (
    <ReactQuill
      value={value}
      onChange={setValue}
      modules={modules}
      placeholder="Start typing..."
      className="trumbowyg-box custom_editor h-300"
    />
  );
};

export default TextEditor2;

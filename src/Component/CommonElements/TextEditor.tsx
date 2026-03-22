import React, { useEffect, useState } from "react";
import Loading from "@/app/loading";
import "react-quill-new/dist/quill.snow.css";

const TextEditor = () => {
  const [ReactQuill, setReactQuill] = useState<any>(null);
  const [value, setValue] = useState("Hello!");

  const modules = {
    toolbar: [
      ["bold", "italic", "underline", "strike"],
      ["blockquote", "code-block"],
      [{ header: 1 }, { header: 2 }],
      [{ list: "ordered" }, { list: "bullet" }],
      [{ script: "sub" }, { script: "super" }],
      [{ indent: "-1" }, { indent: "+1" }],
      [{ direction: "rtl" }],
      [{ size: ["small", false, "large", "huge"] }],
      [{ color: [] }, { background: [] }],
      [
        { align: "" },
        { align: "center" },
        { align: "right" },
        { align: "justify" },
      ],
      ["link", "image", "video"],
      ["clean"],
    ],
  };

  useEffect(() => {
    Promise.all([import("react-quill-new")]).then(([quill]) => {
      setReactQuill(() => quill.default);
    });
  }, []);

  if (!ReactQuill) return <Loading />;

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

export default TextEditor;

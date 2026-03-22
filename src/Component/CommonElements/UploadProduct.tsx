import React, { useState } from "react";
import { FilePond, registerPlugin } from "react-filepond";
import type { FilePondInitialFile } from "filepond";
import FilePondPluginImagePreview from "filepond-plugin-image-preview";
import FilePondPluginFileValidateType from "filepond-plugin-file-validate-type";
import FilePondPluginFileValidateSize from "filepond-plugin-file-validate-size";
import FilePondPluginFileEncode from "filepond-plugin-file-encode";
import FilePondPluginImageExifOrientation from "filepond-plugin-image-exif-orientation";
import "filepond/dist/filepond.min.css";
import "filepond-plugin-image-preview/dist/filepond-plugin-image-preview.css";

registerPlugin(
  FilePondPluginImagePreview,
  FilePondPluginFileValidateType,
  FilePondPluginFileValidateSize,
  FilePondPluginImageExifOrientation,
  FilePondPluginFileEncode
);

const UploadProduct: React.FC = () => {
  const [files, setFiles] = useState<(FilePondInitialFile | Blob | string)[]>(
    []
  );

  return (
    <div>
      <FilePond
        files={files}
        allowMultiple={true}
        maxFiles={5}
        onupdatefiles={(fileItems) =>
          setFiles(fileItems.map((fileItem) => fileItem.file))
        }
        labelIdle='<i class="fa-solid fa-cloud-upload fa-fw fs-4"></i> <div class="filepond--label-action text-decoration-none">Upload Your Product Images</div>'
      />
    </div>
  );
};

export default UploadProduct;

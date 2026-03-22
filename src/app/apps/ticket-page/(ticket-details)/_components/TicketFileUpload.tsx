import React from "react";
import { Card, CardBody, CardHeader } from "reactstrap";
import dynamic from "next/dynamic";
import { FileArrowDown, Folder } from "phosphor-react";
import { GoogleDriveLogo } from "@phosphor-icons/react";
import Loading from "@/app/loading";

// Dynamically import FilePond to avoid SSR
const FilePond = dynamic(
  async () => {
    // Import FilePond plugins first
    import("filepond-plugin-file-validate-type");
    import("filepond-plugin-image-preview");
    import("filepond-plugin-file-encode");
    import("filepond-plugin-file-validate-size");
    import("filepond-plugin-image-exif-orientation");

    const mod = await import("react-filepond");
    return mod.FilePond;
  },
  {
    ssr: false,
    loading: () => <Loading />,
  }
);

const TicketFileUpload = () => {
  return (
    <div>
      <Card>
        <CardHeader>
          <h5>File Upload</h5>
        </CardHeader>
        <CardBody>
          <FilePond
            allowMultiple={true}
            maxFiles={5}
            acceptedFileTypes={["image/jpeg", "image/png", "application/pdf"]}
            maxFileSize="50MB"
            labelIdle={`
              <i className="fa-solid fa-cloud-arrow-up me-2 f-s-30 mb-3 text-primary"></i>
              <p className="f-s-18">Choose a file</p>
              <p className="f-s-14 text-secondary text-center pe-3 ps-3">JPEG, PNG & PDF formats, up to 50MB</p>
              <p className="btn btn-lg file-btn btn-primary mt-3 f-s-14">Choose a Files</p>
            `}
            className="ticket-file-upload app-file-upload"
          />

          {/* File Upload Buttons */}
          <div className="file-upload-btn mt-3">
            <div className="d-flex">
              <span className="bg-danger h-40 w-40 d-flex align-items-center justify-content-center rounded-circle me-3 heartBtn">
                <GoogleDriveLogo size={18} />
              </span>
              <span className="bg-success h-40 w-40 d-flex align-items-center justify-content-center rounded-circle me-3 heartBtn">
                <Folder size={18} />
              </span>
            </div>
            <div>
              <span className="bg-warning h-40 w-40 d-flex align-items-center justify-content-center rounded-circle me-3 heartBtn">
                <FileArrowDown size={18} />
              </span>
            </div>
          </div>
        </CardBody>
      </Card>
    </div>
  );
};

export default TicketFileUpload;

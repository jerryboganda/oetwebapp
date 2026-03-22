import React from "react";
import { Row, Col, Card, CardHeader, CardBody } from "reactstrap";
import { FilePond, registerPlugin } from "react-filepond";
import "filepond/dist/filepond.min.css";
import "filepond-plugin-image-preview/dist/filepond-plugin-image-preview.css";

import FilePondPluginFileValidateType from "filepond-plugin-file-validate-type";
import FilePondPluginImagePreview from "filepond-plugin-image-preview";
import FilePondPluginFileEncode from "filepond-plugin-file-encode";
import FilePondPluginFileValidateSize from "filepond-plugin-file-validate-size";
import FilePondPluginImageExifOrientation from "filepond-plugin-image-exif-orientation";

const SolidFileupload = () => {
  registerPlugin(
    FilePondPluginFileValidateType,
    FilePondPluginImagePreview,
    FilePondPluginFileEncode,
    FilePondPluginFileValidateSize,
    FilePondPluginImageExifOrientation
  );

  const fileUploaders = [
    { id: "solid1", className: "filepondsolid1 file-primary" },
    { id: "solid2", className: "filepondsolid2 file-secondary" },
    { id: "solid3", className: "filepondsolid3 file-success" },
    { id: "solid4", className: "filepondsolid4 file-danger" },
  ];

  return (
    <>
      <Card>
        <CardHeader>
          <h5>Solid Style</h5>
        </CardHeader>
        <CardBody>
          <Row className="file-uploader-box">
            {fileUploaders.map((uploader) => (
              <Col key={uploader.id} sm="6" md="3" xl="3">
                <FilePond
                  allowMultiple={true}
                  name="files"
                  className={`filesolid ${uploader.className}`}
                  data-allow-reorder="true"
                  labelIdle={`<i className="fa-solid fa-cloud-upload fa-fw f-s-25"></i> <div className="filepond--label-action text-decoration-none">Upload Your Files</div>`}
                />
              </Col>
            ))}
          </Row>
        </CardBody>
      </Card>
    </>
  );
};

export default SolidFileupload;

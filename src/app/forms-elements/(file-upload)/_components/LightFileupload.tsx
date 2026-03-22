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

const LightFileupload = () => {
  registerPlugin(
    FilePondPluginFileValidateType,
    FilePondPluginImagePreview,
    FilePondPluginFileEncode,
    FilePondPluginFileValidateSize,
    FilePondPluginImageExifOrientation
  );

  const lightUploaders = [
    { id: "light1", className: "filepondlight1 file-light-primary" },
    { id: "light2", className: "filepondlight2 file-light-secondary" },
    { id: "light3", className: "filepondlight3 file-light-success" },
    { id: "light4", className: "filepondlight4 file-light-danger" },
  ];

  return (
    <>
      <Card>
        <CardHeader>
          <h5>Light Style</h5>
        </CardHeader>
        <CardBody>
          <Row className="file-uploader-box">
            {lightUploaders.map((uploader) => (
              <Col key={uploader.id} sm="6" md="3" xl="3">
                <FilePond
                  allowMultiple={true}
                  name="files"
                  className={`filelight ${uploader.className}`}
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

export default LightFileupload;

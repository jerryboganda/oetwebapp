import React from "react";
import { Row, Col, Card, CardHeader, CardBody } from "reactstrap";
import { FilePond, registerPlugin } from "react-filepond";
import FilePondPluginImagePreview from "filepond-plugin-image-preview";
import FilePondPluginFileEncode from "filepond-plugin-file-encode";
import FilePondPluginFileValidateSize from "filepond-plugin-file-validate-size";
import FilePondPluginImageExifOrientation from "filepond-plugin-image-exif-orientation";
import FilePondPluginFileValidateType from "filepond-plugin-file-validate-type";

// Register the plugin
registerPlugin(
  FilePondPluginFileValidateType,
  FilePondPluginImagePreview,
  FilePondPluginFileEncode,
  FilePondPluginFileValidateSize,
  FilePondPluginImageExifOrientation
);

const FilepondUploader = () => {
  return (
    <>
      <Card className="equal-card">
        <CardHeader>
          <h5>Filepond File Uploader</h5>
        </CardHeader>
        <CardBody>
          <Row className="file-uploader-box">
            <Col sm="6">
              <FilePond
                allowMultiple={false}
                name="file"
                className="filepond-file"
                labelIdle='<i className="fa-solid fa-cloud-upload fa-fw f-s-60 text-secondary"></i> <div className="filepond--label-action text-decoration-none">Upload Your Files</div>'
              />
            </Col>
            <Col sm="6">
              <FilePond
                allowMultiple={true}
                name="files"
                className="filepond-1"
                allowReorder={true}
                labelIdle='<i className="fa-solid fa-cloud-upload fa-fw f-s-60 text-secondary"></i> <div className="filepond--label-action text-decoration-none">Upload Your Files</div>'
              />
            </Col>
          </Row>
        </CardBody>
      </Card>
    </>
  );
};

export default FilepondUploader;

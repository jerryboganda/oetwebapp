import React from "react";
import { Row, Col, Card, CardHeader, CardBody } from "reactstrap";
import FilePondPluginImagePreview from "filepond-plugin-image-preview";
import { FilePond, registerPlugin } from "react-filepond";
import FilePondPluginFileValidateType from "filepond-plugin-file-validate-type";

// Register the plugins with FilePond
registerPlugin(FilePondPluginFileValidateType, FilePondPluginImagePreview);

const CircleUploader: React.FC = () => {
  return (
    <>
      <Card>
        <CardHeader>
          <h5>Circle File Uploader</h5>
        </CardHeader>
        <CardBody>
          <Row className="file-uploader-box">
            <Col className="text-center">
              <FilePond
                allowMultiple={false}
                name="filepond"
                className="filepond-2 m-auto"
                acceptedFileTypes={["image/png", "image/jpeg", "image/gif"]}
                labelIdle="Upload Your Image"
                imagePreviewHeight={170}
                stylePanelLayout="compact circle"
                styleLoadIndicatorPosition="center bottom"
                styleProgressIndicatorPosition="right bottom"
                styleButtonRemoveItemPosition="left bottom"
                styleButtonProcessItemPosition="right bottom"
              />
            </Col>
          </Row>
        </CardBody>
      </Card>
    </>
  );
};

export default CircleUploader;

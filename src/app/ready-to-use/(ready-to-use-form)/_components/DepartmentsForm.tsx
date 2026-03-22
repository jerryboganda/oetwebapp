import { useState } from "react";
import {
  Card,
  CardBody,
  CardHeader,
  Row,
  Col,
  Form,
  FormGroup,
  Label,
  Input,
  Button,
} from "reactstrap";
import { FilePond, registerPlugin } from "react-filepond";
import FilePondPluginImagePreview from "filepond-plugin-image-preview";
import FilePondPluginFileValidateType from "filepond-plugin-file-validate-type";

const DepartmentsForm = () => {
  registerPlugin(FilePondPluginFileValidateType, FilePondPluginImagePreview);

  const [files, setFiles] = useState<(Blob | File | string)[]>([]);

  return (
    <>
      <Card>
        <CardHeader>
          <h5>Add Departments Form</h5>
        </CardHeader>
        <CardBody>
          <Form className="app-form">
            <Row>
              <Col md="6">
                <FormGroup>
                  <Label>Department Name</Label>
                  <Input type="text" placeholder="Enter Department" />
                </FormGroup>
              </Col>

              <Col md="6">
                <FormGroup>
                  <Label>Email Address</Label>
                  <Input type="email" placeholder="@department.com" />
                </FormGroup>
              </Col>

              <Col md="6" xl="8">
                <FormGroup>
                  <Label>Department Head</Label>
                  <Input type="text" placeholder="Enter Department Head" />
                </FormGroup>
              </Col>

              <Col md="6" xl="4">
                <FormGroup>
                  <Label>Add Employee</Label>
                  <Input type="select">
                    <option value="">Select Employee</option>
                    <option value="1">Cedric Kelly</option>
                    <option value="2">Haley Kennedy</option>
                    <option value="3">Angelica Ramos</option>
                    <option value="4">Ashton Cox</option>
                    <option value="5">Timothy Mooney</option>
                    <option value="6">Brenden Wagner</option>
                  </Input>
                </FormGroup>
              </Col>

              <Col xs="12">
                <FormGroup>
                  <Label>Upload Files</Label>
                  <FilePond
                    className="filepond-1"
                    files={files}
                    onupdatefiles={(fileItems) =>
                      setFiles(fileItems.map((fileItem) => fileItem.file))
                    }
                    allowMultiple
                    maxFiles={5}
                    labelIdle='<i class="ti ti-cloud-upload fs-4"></i> <div class="filepond--label-action text-decoration-none">Upload Your Deparment Images</div>'
                  />
                </FormGroup>
              </Col>

              <Col xs="12">
                <FormGroup>
                  <Label>Department Bio</Label>
                  <Input
                    type="textarea"
                    rows="3"
                    placeholder="Enter Department Bio"
                  />
                </FormGroup>
              </Col>

              <Col xs="12" className="text-end">
                <Button color="primary" type="submit" className="me-2">
                  Add
                </Button>
                <Button color="secondary" type="reset">
                  Reset
                </Button>
              </Col>
            </Row>
          </Form>
        </CardBody>
      </Card>
    </>
  );
};

export default DepartmentsForm;

import React from "react";
import { Container } from "reactstrap";
import Breadcrumbs from "@/Component/CommonElements/Breadcrumbs";
import TextEditor from "@/Component/CommonElements/TextEditor";
import TextEditor2 from "@/Component/CommonElements/TextEditor2";
import { IconBriefcase } from "@tabler/icons-react";

const EditorPage = () => {
  return (
    <div>
      <Container fluid>
        <Breadcrumbs
          mainTitle="Editor"
          title="Ui Kits"
          path={["Editor"]}
          Icon={IconBriefcase}
        />
        <div className="row">
          <div className="col-12">
            <div className="card">
              <div className="card-body">
                <TextEditor />
              </div>
            </div>
          </div>
          <div className="col-12">
            <div className="card">
              <div className="card-body">
                <TextEditor2 />
              </div>
            </div>
          </div>
        </div>
      </Container>
    </div>
  );
};

export default EditorPage;

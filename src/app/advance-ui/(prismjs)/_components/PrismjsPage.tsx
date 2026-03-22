import React from "react";
import { Card, CardBody, CardHeader, Col, Container, Row } from "reactstrap";
import Breadcrumbs from "@/Component/CommonElements/Breadcrumbs";
import "prismjs/themes/prism.css";
import { IconBriefcase } from "@tabler/icons-react";
import PrismCodeWrapper from "@/Component/CommonElements/PrismCodeWrapper";

const PrismjsPage = () => {
  const codeSnippets = {
    html: `<div class="container-fluid">
    <div class="row">
        <div class="col-md-6">
            <div class="card">
                <div class="card-header code-header" href="#collapseExample" aria-expanded="false" aria-controls="collapseExample">
                    <h5>Select by class</h5>
                      <IconCode size={32} data-source="dlb1" />
                </div>
                <div class="card-body">
                    <select class="select1" multiple>
                        <option value="1">One</option>
                        <option value="2">Two</option>
                        <option value="3">Three</option>
                    </select>
                </div>
            </div>
        </div>
    </div>
</div>`,

    css: `.dual-listbox .dual-listbox__button {
    border-radius: 5px;
    background-color: rgba(var(--primary), 0.9);
    padding: 7px 12px;
}
.dual-listbox .dual-listbox__button:hover {
    background-color: rgba(var(--primary), 1);
    transition: all 0.3s ease;
}
.dual-listbox .dual-listbox__item.dual-listbox__item--selected {
    background-color: rgba(var(--primary), 0.1);
    color: rgba(var(--primary), 1);
}`,

    scss: `.dual-listbox {
    width: 100%;
    .dual-listbox__container {
        display: flex;
        align-items: center;
        justify-content: space-between;
        flex-wrap: unset;
    }
    .dual-listbox__search {
        color: rgba(var(--secondary), 1);
        padding: 0.4rem 0.75rem;
        border: 1px solid var(--border_color);
        border-radius: 0.3rem;
        &:focus {
            color: rgba(var(--dark), 1);
            box-shadow: 0 0 0 0.25rem rgba(var(--primary), 0.3);
            border: 1px solid rgba(var(--primary), 1);
            outline: none;
        }
    }
    .dual-listbox__title {
        font-weight: 600;
        color: rgba(var(--dark), 0.60);
        padding: 0.6rem 1rem;
        border-radius: 0.3rem 0.3rem 0 0;
        border-color: rgba(var(--dark), 0.20);
    }
    .dual-listbox__available, .dual-listbox__selected {
        border-radius: 0 0 0.3rem 0.3rem;
        border: 1px solid rgba(var(--dark), 0.20);
    }
}`,

    javascript: `var dlb4 = new DualListbox(".select4", {
    showSortButtons: true,
});
var sources = document.querySelectorAll(".source");
for (var i = 0; i < sources.length; i++) {
    var source = sources[i];
    source.addEventListener("click", function(event) {
        var code = document.querySelector("." + event.currentTarget.dataset.source);
        code.classList.toggle("open");
    });
}`,
  };

  return (
    <>
      <Container fluid>
        <Breadcrumbs
          mainTitle="Prismjs"
          title="Advance Ui"
          path={["Prismjs"]}
          Icon={IconBriefcase}
        />

        <PrismCodeWrapper>
          <Row>
            <Col xxl={6} xl={6} lg={12}>
              <Row>
                <Col xs={12}>
                  <Card>
                    <CardHeader>
                      <h6>Dual Listboxes-html</h6>
                    </CardHeader>
                    <CardBody>
                      <pre>
                        <code className="language-html">
                          {codeSnippets.html}
                        </code>
                      </pre>
                    </CardBody>
                  </Card>
                </Col>
                <Col xs={12}>
                  <Card>
                    <CardHeader>
                      <h6>Dual Listboxes-css</h6>
                    </CardHeader>
                    <CardBody>
                      <pre>
                        <code className="language-css">{codeSnippets.css}</code>
                      </pre>
                    </CardBody>
                  </Card>
                </Col>
              </Row>
            </Col>
            <Col xxl={6} xl={6} lg={12}>
              <Row>
                <Col xs={12}>
                  <Card>
                    <CardHeader>
                      <h6>Dual Listboxes-scss</h6>
                    </CardHeader>
                    <CardBody>
                      <pre className="h-390">
                        <code className="language-css">
                          {codeSnippets.scss}
                        </code>
                      </pre>
                    </CardBody>
                  </Card>
                </Col>
                <Col xs={12}>
                  <Card>
                    <CardHeader>
                      <h6>Dual Listboxes-javascript</h6>
                    </CardHeader>
                    <CardBody>
                      <pre>
                        <code className="language-javascript">
                          {codeSnippets.javascript}
                        </code>
                      </pre>
                    </CardBody>
                  </Card>
                </Col>
              </Row>
            </Col>
          </Row>
        </PrismCodeWrapper>
      </Container>
    </>
  );
};

export default PrismjsPage;

import React from "react";
import Breadcrumbs from "@/Component/CommonElements/Breadcrumbs";
import "prismjs/themes/prism.css";
import { Card, CardBody, CardHeader, Col, Container, Row } from "reactstrap";
import { IconBriefcase, IconCode } from "@tabler/icons-react";
import UncontrolledCollapseWrapper from "@/Component/CommonElements/UncontrolledCollapseWrapper";
import PrismCodeWrapper from "@/Component/CommonElements/PrismCodeWrapper";

const dividerStyles = ["solid", "dotted", "dashed"];

const alignments = [
  "justify-content-start",
  "justify-content-center",
  "justify-content-end",
];

const DividerPage = () => {
  const colorsDivider = [
    {
      class: "secondary justify-content-center",
      element: (
        <span className="badge text-bg-secondary">Login With Social</span>
      ),
      code: `<span className="badge text-bg-secondary">Login With Social</span>`,
    },
    {
      class: "success justify-content-end",
      element: <p className="text-success">Align Right</p>,
      code: `<p className="text-success">Align Right</p>`,
    },
    {
      class: "danger",
      element: (
        <span className="badge text-bg-danger">Choose from below option</span>
      ),
      code: `<span className="badge text-bg-danger">Choose from below option</span>`,
    },
    {
      class: "info justify-content-center gap-1",
      element: (
        <>
          <button type="button" className="btn btn-facebook icon-btn b-r-22">
            <i className="ti ti-brand-facebook text-white"></i>
          </button>
          <button type="button" className="btn btn-twitter icon-btn b-r-22">
            <i className="ti ti-brand-twitter text-white"></i>
          </button>
          <button type="button" className="btn btn-linkedin icon-btn b-r-22">
            <i className="ti ti-brand-linkedin text-white"></i>
          </button>
        </>
      ),
      code: `
<button type="button" className="btn btn-facebook icon-btn b-r-22">
  <i className="ti ti-brand-facebook text-white"></i>
</button>
<button type="button" className="btn btn-twitter icon-btn b-r-22">
  <i className="ti ti-brand-twitter text-white"></i>
</button>
<button type="button" className="btn btn-linkedin icon-btn b-r-22">
  <i className="ti ti-brand-linkedin text-white"></i>
</button>`,
    },
    {
      class: "warning justify-content-end gap-1",
      element: null,
      code: ``,
    },
    {
      class: "dark justify-content-center",
      element: <p>Dark</p>,
      code: `<p>Dark</p>`,
    },
  ];

  const colorsHorizontal = [
    "primary",
    "secondary",
    "success",
    "danger",
    "warning",
    "info",
    "light",
    "dark",
  ];

  return (
    <div>
      <Container fluid>
        <Breadcrumbs
          mainTitle="Divider"
          title="Ui Kits"
          path={["divider"]}
          Icon={IconBriefcase}
        />
        <PrismCodeWrapper>
          <Row>
            <Col md="6" xl="8">
              <Card>
                <CardHeader className="code-header">
                  <h5>Vertical Dividers</h5>
                  <a id="togglerDivider1">
                    <IconCode className="source cursor-pointer" size={32} />
                  </a>
                </CardHeader>
                <CardBody>
                  {dividerStyles.map((style, index) => (
                    <div key={index} className={`app-divider-v ${style}`}></div>
                  ))}
                  <UncontrolledCollapseWrapper toggler="#togglerDivider1">
                    <pre>
                      <code className="language-html">
                        {`
  
    ${dividerStyles.map((style) => `<div className="app-divider-v ${style}"></div>`).join("\n    ")}
 `}
                      </code>
                    </pre>
                  </UncontrolledCollapseWrapper>
                </CardBody>
              </Card>
            </Col>

            <Col md="6" xl="4">
              <Card className="h-100">
                <CardHeader className="code-header d-flex justify-content-between">
                  <h5>Horizontal</h5>
                  <a id="togglerDivider2">
                    <IconCode className="source cursor-pointer" size={32} />
                  </a>
                </CardHeader>
                <CardBody className="divider-body">
                  <div className="d-flex h-100">
                    {dividerStyles.map((style, index) => (
                      <div
                        key={index}
                        className={`app-divider-h ${style}`}
                      ></div>
                    ))}
                  </div>
                </CardBody>
                <UncontrolledCollapseWrapper toggler="#togglerDivider2">
                  <pre>
                    <code className="language-html">
                      {`
            ${dividerStyles
              .map((style) => `<div className="app-divider-h ${style}"></div>`)
              .join("\n            ")}
                     `}
                    </code>
                  </pre>
                </UncontrolledCollapseWrapper>
              </Card>
            </Col>

            <Col md="6" xl="8">
              <Card>
                <CardHeader className="code-header">
                  <h5>Divider with text & aligns</h5>
                  <a id="togglerDivider3">
                    <IconCode className="source cursor-pointer" size={32} />
                  </a>
                </CardHeader>
                <CardBody>
                  {alignments.map((alignment, index) => (
                    <div key={index} className={`app-divider-v ${alignment}`}>
                      <p>{alignment}</p>
                    </div>
                  ))}
                  <UncontrolledCollapseWrapper toggler="togglerDivider3">
                    <pre>
                      <code className="language-html">
                        {`
 ${alignments.map((alignment) => `<div class="app-divider-v ${alignment}"><p>${alignment}</p></div>`).join("\n  ")}
`}
                      </code>
                    </pre>
                  </UncontrolledCollapseWrapper>
                </CardBody>
              </Card>
            </Col>

            <Col md="6" xl="4">
              <Card className="h-100">
                <CardHeader className="code-header d-flex justify-content-between">
                  <h5>Horizontal with text & aligns</h5>
                  <a id="togglerDivider4">
                    <IconCode className="source cursor-pointer" size={32} />
                  </a>
                </CardHeader>
                <CardBody className="divider-body d-flex">
                  <div className="app-divider-h">
                    <p>Or</p>
                  </div>
                  <div className="app-divider-h align-items-center">
                    <p>align-items-center</p>
                  </div>
                  <div className="app-divider-h align-items-end">
                    <p>Or</p>
                  </div>
                </CardBody>
                <UncontrolledCollapseWrapper toggler="togglerDivider4">
                  <pre>
                    <code className="language-html">
                      {`
 <div class="app-divider-h"><p>Or</p></div>
 <div class="app-divider-h align-items-center"><p>align-items-center</p></div>
 <div class="app-divider-h align-items-end"><p>Or</p></div>
          `}
                    </code>
                  </pre>
                </UncontrolledCollapseWrapper>
              </Card>
            </Col>

            <Col md="6" xl="8">
              <Card>
                <CardHeader className="code-header">
                  <h5>Color Options With Icon Buttons</h5>
                  <a id="togglerDivider5">
                    <IconCode className="source cursor-pointer" size={32} />
                  </a>
                </CardHeader>
                <CardBody>
                  {colorsDivider.map((item, idx) => (
                    <div key={idx} className={`app-divider-v ${item.class}`}>
                      {item.element}
                    </div>
                  ))}
                </CardBody>
                <UncontrolledCollapseWrapper toggler="togglerDivider5">
                  <pre>
                    <code className="language-html">
                      {`
      ${colorsDivider.map((item) => `<div className="app-divider-v ${item.class}">${item.code}</div>`).join("\n      ")}
    `}
                    </code>
                  </pre>
                </UncontrolledCollapseWrapper>
              </Card>
            </Col>

            <Col md="6" xl="4">
              <Card className="h-100">
                <CardHeader className="code-header">
                  <h5>Horizontal</h5>
                  <a id="togglerDivider6">
                    <IconCode className="source cursor-pointer" size={32} />
                  </a>
                </CardHeader>
                <CardBody className="divider-body d-flex">
                  {colorsHorizontal.map((color, index) => (
                    <div key={index} className={`app-divider-h ${color}`}></div>
                  ))}
                </CardBody>
                <UncontrolledCollapseWrapper toggler="togglerDivider6">
                  <pre>
                    <code className="language-html">
                      {`
  ${colorsHorizontal
    .map((color) => `<div className="app-divider-h ${color}"></div>`)
    .join("\n      ")}
`}
                    </code>
                  </pre>
                </UncontrolledCollapseWrapper>
              </Card>
            </Col>
          </Row>
        </PrismCodeWrapper>
      </Container>
    </div>
  );
};

export default DividerPage;

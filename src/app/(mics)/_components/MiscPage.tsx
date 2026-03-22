import React from "react";
import { Card, CardBody, CardHeader, Col, Container, Row } from "reactstrap";
import Breadcrumbs from "@/Component/CommonElements/Breadcrumbs";
import { AppleShortcuts } from "iconoir-react";
import {
  IconBooks,
  IconClipboardData,
  IconDatabaseExport,
  IconFileFilled,
  IconHome,
  IconInfoCircle,
  IconTableExport,
  IconUsers,
} from "@tabler/icons-react";
const iconList = [
  <IconHome key="home" size={16} />,
  <IconBooks key="books" size={16} />,
  <IconFileFilled key="file" size={16} />,
  <IconDatabaseExport key="db" size={16} />,
];

const textList = ["page1", "page2", "page3", "page4"];
const simplePages = ["Page 1", "Page 2", "Page 3", "Page 4", "Page 5"];
const breadcrumbItems = ["Page 1", "Page 2", "Page 3", "Page 4", "Page 5"];
const breadcrumbTexts = ["Home", "Gallery", "Library", "Web", "Data"];
const breadcrumbIcons = [
  <IconHome key="home" size={18} className="mg-e-4 f-s-18" />,
  <IconBooks key="books" size={18} className="mg-e-4 f-s-18" />,
  <IconFileFilled key="file" size={18} className="mg-e-4 f-s-18" />,
  <IconDatabaseExport key="db" size={18} className="mg-e-4 f-s-18" />,
  <IconClipboardData key="clipboard" size={18} className="mg-e-4 f-s-18" />,
];
const breadcrumbIconsOnly = [
  <IconHome key="home" size={22} className="mg-e-4 f-s-22" />,
  <IconBooks key="books" size={22} className="mg-e-4 f-s-22" />,
  <IconFileFilled key="file" size={22} className="mg-e-4 f-s-22" />,
  <IconDatabaseExport key="db" size={22} className="mg-e-4 f-s-22" />,
  <IconClipboardData key="clipboard" size={22} className="mg-e-4 f-s-22" />,
];
const stepNumbers = ["1", "2", "3"];
const pages = [1, 2, 3];
const iconSteps = [
  <IconUsers key="users" />,
  <IconInfoCircle key="info" />,
  <IconTableExport key="export" />,
];
const shapeSteps = [
  { label: "Cart" },
  { label: "Billing" },
  { label: "Shipping" },
  { label: "Payment" },
];
const breadcrumbSets: BreadcrumbSet[] = [
  {
    className: "breadcrumb bg-light-secondary p-2",
    items: [
      {
        label: "Home",
        href: "#",
        icon: <IconHome size={18} className="me-1" />,
      },
      {
        label: "Library",
        iconClass: "ti ti-books",
        active: true,
      },
    ],
  },
  {
    className: "breadcrumb bg-light-secondary p-2",
    items: [
      { label: "Home", href: "#" },
      { label: "Library", active: true },
    ],
  },
  {
    className: "breadcrumb flex-wrap bg-light-secondary p-2",
    items: [
      {
        label: "Home",
        href: "#",
        icon: <IconHome size={18} className="me-1" />,
      },
      {
        label: "Library",
        href: "#",
        iconClass: "ti ti-books",
      },
      {
        label: "File",
        iconClass: "ti ti-file-filled",
        active: true,
      },
    ],
  },
  {
    className: "breadcrumb bg-light-secondary p-2 bootstrap-breadcrumb divider",
    items: [
      { label: "Home", href: "#" },
      { label: "Library", active: true },
    ],
  },
  {
    className:
      "breadcrumb bg-light-secondary p-2 mb-0 bootstrap-breadcrumb divider1",
    items: [
      {
        label: "Home",
        href: "#",
        icon: <IconHome size={18} className="me-1" />,
      },
      {
        label: "Library",
        iconClass: "ti ti-books",
        active: true,
      },
    ],
  },
];
type BreadcrumbItem = {
  label: string;
  href?: string;
  icon?: React.ReactNode;
  iconClass?: string;
  active?: boolean;
};

// Type for a breadcrumb list group
type BreadcrumbSet = {
  className: string;
  items: BreadcrumbItem[];
};
const MiscPage = () => {
  return (
    <div>
      <Container fluid>
        <Breadcrumbs
          mainTitle="Misc"
          title="Misc Pages"
          path={["Misc"]}
          Icon={AppleShortcuts}
        />

        <Row>
          <Col lg="5">
            <Card>
              <CardHeader>
                <h5>Breadcrumbs</h5>
              </CardHeader>
              <CardBody className="app-breadcrumb">
                {breadcrumbSets.map((set, index) => (
                  <div key={index}>
                    <ol className={set.className}>
                      {set.items.map((item, i) => (
                        <li
                          key={i}
                          className={`breadcrumb-item${item.active ? " active" : ""}`}
                          aria-current={item.active ? "page" : undefined}
                        >
                          {item.href ? (
                            <a
                              href={item.href}
                              className="d-flex align-content-center"
                            >
                              {item.icon ||
                                (item.iconClass && (
                                  <i className={`${item.iconClass} me-2`} />
                                ))}
                              {item.label}
                            </a>
                          ) : (
                            <>
                              {item.icon ||
                                (item.iconClass && (
                                  <i className={`${item.iconClass} me-2`} />
                                ))}
                              {item.label}
                            </>
                          )}
                        </li>
                      ))}
                    </ol>
                  </div>
                ))}
              </CardBody>
            </Card>
          </Col>

          <Col lg="7">
            <Card className="equal-card">
              <CardHeader>
                <h5>Custom Breadcrumbs</h5>
              </CardHeader>
              <CardBody>
                <div>
                  <ul className="line-breadcrumbs">
                    {textList.map((text, index) => (
                      <li key={index}>
                        <a className={index < 2 ? "active" : ""} href="#">
                          {text}
                        </a>
                      </li>
                    ))}
                  </ul>
                </div>
                <div className="mt-4">
                  <ul className="breadcrumbs">
                    {textList.map((text, index) => (
                      <li key={index}>
                        <a className={index < 2 ? "active" : ""} href="#">
                          {iconList[index]} {text}
                        </a>
                      </li>
                    ))}
                  </ul>
                </div>
                <div className="mt-4">
                  <ul className="circle-breadcrumbs breadcrumbs-primary">
                    {iconList.map((icon, index) => (
                      <li key={index} className={index === 0 ? "active" : ""}>
                        <a href="#">{icon}</a>
                      </li>
                    ))}
                  </ul>
                </div>
                <div className="mt-4">
                  <ul className="simple-breadcrumbs">
                    {simplePages.map((page, index) => (
                      <li key={index} className={index === 0 ? "active" : ""}>
                        <a href="#">{page}</a>
                      </li>
                    ))}
                  </ul>
                </div>
                <div className="mt-4">
                  <ul className="simple-shape-breadcrumbs">
                    {simplePages.map((page, index) => (
                      <li key={index} className={index < 2 ? "active" : ""}>
                        <a href="#">{page}</a>
                      </li>
                    ))}
                  </ul>
                </div>
              </CardBody>
            </Card>
          </Col>

          <Col xl="6">
            <Card className="equal-card">
              <CardHeader className="card-header">
                <h5>Custom Breadcrumb</h5>
              </CardHeader>
              <CardBody>
                <div className="mb-3">
                  <ul className="shape-breadcrumbs">
                    {breadcrumbItems.map((item, index) => (
                      <li key={index} className={index < 2 ? "active" : ""}>
                        <a href="#">{item}</a>
                      </li>
                    ))}
                  </ul>
                </div>
                <div>
                  <ul className="shape-breadcrumbs">
                    {breadcrumbItems.map((item, index) => (
                      <li key={index} className={index < 3 ? "active" : ""}>
                        <a href="#">
                          {index === 1}
                          {item}
                        </a>
                      </li>
                    ))}
                  </ul>
                </div>
              </CardBody>
            </Card>
          </Col>

          <Col xl="6">
            <Card className="equal-card">
              <CardHeader className="card-header">
                <h5>Rounded Breadcrumb</h5>
              </CardHeader>
              <CardBody>
                <div className="mb-3">
                  <ul className="rounded-breadcrumbs">
                    {breadcrumbTexts.map((text, index) => (
                      <li key={index} className={index === 4 ? "active" : ""}>
                        {index !== 4 ? <a href="#">{text}</a> : text}
                      </li>
                    ))}
                  </ul>
                </div>
                <div className="mb-3">
                  <ul className="rounded-breadcrumbs">
                    {breadcrumbTexts.map((text, index) => (
                      <li key={index} className={index === 4 ? "active" : ""}>
                        {index !== 4 ? (
                          <a href="#">
                            {breadcrumbIcons[index]}
                            {text}
                          </a>
                        ) : (
                          <>
                            {breadcrumbIcons[index]}
                            {text}
                          </>
                        )}
                      </li>
                    ))}
                  </ul>
                </div>
                <div>
                  <ul className="rounded-breadcrumbs">
                    {breadcrumbIconsOnly.map((icon, index) => (
                      <li key={index} className={index === 4 ? "active" : ""}>
                        {index !== 4 ? <a href="#">{icon}</a> : icon}
                      </li>
                    ))}
                  </ul>
                </div>
              </CardBody>
            </Card>
          </Col>

          <Col xl="6">
            <Card>
              <CardHeader className="card-header">
                <h5>Steps</h5>
              </CardHeader>
              <CardBody>
                <div className="form-wizard">
                  <div className="form-wizard-header">
                    <ul className="form-wizard-steps">
                      {stepNumbers.map((step, index) => (
                        <li key={index} className={index === 0 ? "active" : ""}>
                          <span className="wizard-steps">{step}</span>
                        </li>
                      ))}
                    </ul>
                  </div>
                </div>
                <div className="form-wizard">
                  <div className="form-wizard-header">
                    <ul className="form-wizard-steps">
                      {iconSteps.map((icon, index) => (
                        <li key={index} className={index === 0 ? "active" : ""}>
                          <span className="wizard-steps circle-steps">
                            {icon}
                          </span>
                        </li>
                      ))}
                    </ul>
                  </div>
                </div>
                <div className="mb-3">
                  <ul className="shape-step">
                    {shapeSteps.map((step, index) => (
                      <li key={index} className={index < 2 ? "active" : ""}>
                        <a href="#">{step.label}</a>
                      </li>
                    ))}
                  </ul>
                </div>
              </CardBody>
            </Card>
          </Col>

          <Col lg={6}>
            <Card className="equal-card">
              <CardHeader className="card-header">
                <h5>Pagination</h5>
              </CardHeader>
              <CardBody>
                {/* Pagination with text nav */}
                <div className="app-pagination-link">
                  <ul className="pagination app-pagination">
                    <li className="page-item">
                      <a className="page-link b-r-left" href="#">
                        Previous
                      </a>
                    </li>
                    {pages.map((page) => (
                      <li className="page-item" key={`nav1-${page}`}>
                        <a className="page-link" href="#">
                          {page}
                        </a>
                      </li>
                    ))}
                    <li className="page-item">
                      <a className="page-link" href="#">
                        Next
                      </a>
                    </li>
                  </ul>
                </div>

                {/* Pagination with arrows */}
                <div className="mt-3">
                  <ul className="pagination app-pagination">
                    <li className="page-item">
                      <a className="page-link" href="#" aria-label="Previous">
                        <span aria-hidden="true">&laquo;</span>
                      </a>
                    </li>
                    {pages.map((page) => (
                      <li className="page-item" key={`nav2-${page}`}>
                        <a className="page-link" href="#">
                          {page}
                        </a>
                      </li>
                    ))}
                    <li className="page-item">
                      <a className="page-link" href="#" aria-label="Next">
                        <span aria-hidden="true">&raquo;</span>
                      </a>
                    </li>
                  </ul>
                </div>

                {/* Pagination with active + disabled */}
                <div className="mt-3">
                  <ul className="pagination app-pagination">
                    <li className="page-item disabled">
                      <a href="#" className="page-link b-r-left">
                        Previous
                      </a>
                    </li>
                    {pages.map((page) => (
                      <li
                        className={`page-item${page === 2 ? " active" : ""}`}
                        key={`nav3-${page}`}
                        aria-current={page === 2 ? "page" : undefined}
                      >
                        <a className="page-link" href="#">
                          {page}
                        </a>
                      </li>
                    ))}
                    <li className="page-item">
                      <a className="page-link" href="#">
                        Next
                      </a>
                    </li>
                  </ul>
                </div>

                {/* Large pagination, justify end */}
                <div className="mt-3">
                  <div>
                    <ul className="pagination pagination-lg justify-content-end app-pagination">
                      <li className="page-item disabled">
                        <a href="#" className="page-link b-r-left">
                          «
                        </a>
                      </li>
                      {pages.map((page) => (
                        <li
                          className={`page-item${page === 2 ? " active" : ""}${page === 3 ? " d-none d-sm-block" : ""}`}
                          key={`nav4-${page}`}
                          aria-current={page === 2 ? "page" : undefined}
                        >
                          <a className="page-link" href="#">
                            {page}
                          </a>
                        </li>
                      ))}
                      <li className="page-item">
                        <a className="page-link b-r-right" href="#">
                          »
                        </a>
                      </li>
                    </ul>
                  </div>
                </div>
              </CardBody>
            </Card>
          </Col>
        </Row>
      </Container>
    </div>
  );
};

export default MiscPage;

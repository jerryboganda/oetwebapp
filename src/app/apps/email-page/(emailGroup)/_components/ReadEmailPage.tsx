"use client";
import React, { useState } from "react";
import {
  Button,
  Card,
  CardBody,
  Col,
  Container,
  Dropdown,
  DropdownItem,
  DropdownMenu,
  DropdownToggle,
  Row,
  Tooltip,
} from "reactstrap";
import Link from "next/link";
import TextEditor from "@/Component/CommonElements/TextEditor";
import {
  IconAlertOctagon,
  IconArchive,
  IconArrowBackUp,
  IconArrowBackUpDouble,
  IconArrowForwardUp,
  IconArrowLeft,
  IconChevronDown,
  IconChevronLeft,
  IconChevronRight,
  IconDotsVertical,
  IconDownload,
  IconFolder,
  IconMailOpened,
  IconPaperclip,
  IconStack2,
  IconStar,
  IconTag,
  IconTrash,
} from "@tabler/icons-react";
import Breadcrumbs from "@/Component/CommonElements/Breadcrumbs";

const ReadEmailPage = () => {
  const [tooltipOpen, setTooltipOpen] = useState<{ [key: string]: boolean }>({
    "tooltip-archive": false,
  });
  const [hoverDropdownOpen, setHoverDropdownOpen] = useState(false);
  const [dotsDropdownOpen, setDotsDropdownOpen] = useState(false);

  const toggleTooltip = (id: string) => {
    setTooltipOpen((prev) => ({
      ...prev,
      [id]: !prev[id],
    }));
  };

  const files = [
    {
      icon: "/images/icons/file.png",
      title: "Meeting Paper's",
      details: "1MB",
    },
    {
      icon: "/images/icons/folder.png",
      title: "Project Details",
      details: "18 Files",
    },
  ];

  return (
    <Container fluid>
      <Breadcrumbs
        mainTitle="Read Email"
        title="Apps"
        path={["Emails App", "Read Email"]}
        Icon={IconStack2}
      />
      <Row>
        <Col xs={12}>
          <Card>
            <CardBody>
              <div className="d-flex align-items-center flex-wrap mb-3">
                <div className="flex-grow-1">
                  <Link
                    href="/apps/email-page/email"
                    role="button"
                    className="btn p-0 pe-2"
                    title="Back To Inbox"
                  >
                    <IconArrowLeft size={22} className="text-dark" />
                  </Link>
                  <Button
                    color="link"
                    className="border-0 p-1 text-decoration-none"
                    id="tooltip-archive"
                  >
                    <IconArchive size={18} className="text-dark" />
                  </Button>
                  <Tooltip
                    target="tooltip-archive"
                    isOpen={tooltipOpen["tooltip-archive"] || false}
                    toggle={() => toggleTooltip("tooltip-archive")}
                  >
                    Archive
                  </Tooltip>

                  <Button
                    color="link"
                    className="border-0 p-1 text-decoration-none"
                    id="tooltip-star"
                  >
                    <IconStar size={18} className="text-dark" />
                  </Button>
                  <Tooltip
                    target="tooltip-star"
                    isOpen={tooltipOpen["tooltip-star"] || false}
                    toggle={() => toggleTooltip("tooltip-star")}
                  >
                    Starred
                  </Tooltip>

                  <Button
                    color="link"
                    className="border-0 p-1 text-decoration-none"
                    id="tooltip-spam"
                  >
                    <IconAlertOctagon size={18} className="text-dark" />
                  </Button>
                  <Tooltip
                    target="tooltip-spam"
                    isOpen={tooltipOpen["tooltip-spam"] || false}
                    toggle={() => toggleTooltip("tooltip-spam")}
                  >
                    Spam
                  </Tooltip>

                  <Button
                    color="link"
                    className="border-0 p-1 text-decoration-none"
                    id="tooltip-delete"
                  >
                    <IconTrash size={18} className="text-dark" />
                  </Button>
                  <Tooltip
                    target="tooltip-delete"
                    isOpen={tooltipOpen["tooltip-delete"] || false}
                    toggle={() => toggleTooltip("tooltip-delete")}
                  >
                    Delete
                  </Tooltip>

                  <Button
                    color="link"
                    className="border-0 p-1 text-decoration-none"
                    id="tooltip-folder"
                  >
                    <IconFolder size={18} className="text-dark" />
                  </Button>
                  <Tooltip
                    target="tooltip-folder"
                    isOpen={tooltipOpen["tooltip-folder"] || false}
                    toggle={() => toggleTooltip("tooltip-folder")}
                  >
                    Folder
                  </Tooltip>

                  <Button
                    color="link"
                    className="border-0 p-1 text-decoration-none"
                    id="tooltip-labels"
                  >
                    <IconTag size={18} className="text-dark" />
                  </Button>
                  <Tooltip
                    target="tooltip-labels"
                    isOpen={tooltipOpen["tooltip-labels"] || false}
                    toggle={() => toggleTooltip("tooltip-labels")}
                  >
                    Labels
                  </Tooltip>
                </div>

                {/* Right Section */}
                <div className="d-flex justify-content-end">
                  <span className="text-muted text-dark">2 to 10</span>
                  <Button
                    color="link"
                    className="border-0 p-1 text-decoration-none"
                    id="tooltip-next"
                  >
                    <IconChevronLeft size={22} className="text-dark" />
                  </Button>
                  <Tooltip
                    target="tooltip-next"
                    isOpen={tooltipOpen["tooltip-next"] || false}
                    toggle={() => toggleTooltip("tooltip-next")}
                  >
                    Next
                  </Tooltip>
                  <Button
                    color="link"
                    className="border-0 p-1 text-decoration-none"
                    id="tooltip-previous"
                  >
                    <IconChevronRight size={22} className="text-dark" />
                  </Button>
                  <Tooltip
                    target="tooltip-previous"
                    isOpen={tooltipOpen["tooltip-previous"] || false}
                    toggle={() => toggleTooltip("tooltip-previous")}
                  >
                    Previous
                  </Tooltip>
                </div>
              </div>

              <div className="mail-container">
                <div className="d-flex align-items-center flex-nowrap mb-5">
                  <span className="bg-secondary h-45 w-45 d-flex-center b-r-50 position-relative ">
                    <img
                      src="/images/avatar/6.png"
                      alt=""
                      className="img-fluid b-r-50"
                    />
                    <span className="position-absolute top-0 d-flex-center bg-success border border-light rounded-circle text-center p-1 f-s-10 end-0"></span>
                  </span>
                  <div className="flex-grow-1 ps-2">
                    <p className="text-muted f-s-14 m-0">
                      bettehagenes@gmail.com
                    </p>
                    <div className="btn-group hover-dropdown">
                      <Dropdown
                        isOpen={hoverDropdownOpen}
                        toggle={() => setHoverDropdownOpen(!hoverDropdownOpen)}
                        className="d-inline-block"
                      >
                        <DropdownToggle
                          color="link"
                          className="btn waves-effect waves-light text-dark p-0"
                        >
                          to
                          <IconChevronDown size={18} />
                        </DropdownToggle>
                        <DropdownMenu className="dropdown-menu">
                          <DropdownItem className="dropdown-item">
                            From :{" "}
                            <span className="text-muted f-s-14">
                              arteam@gmail.com
                            </span>
                          </DropdownItem>
                          <DropdownItem className="dropdown-item">
                            To :{" "}
                            <span className="text-muted f-s-14">
                              bettehagenes@gmail.com
                            </span>
                          </DropdownItem>
                          <DropdownItem className="dropdown-item">
                            cc :{" "}
                            <span className="text-muted f-s-14">
                              bettehagenes@gmail.com
                            </span>
                          </DropdownItem>
                          <DropdownItem className="dropdown-item">
                            Date :{" "}
                            <span className="text-muted f-s-14">
                              29 Sep 2024
                            </span>
                          </DropdownItem>
                          <DropdownItem className="dropdown-item">
                            Subject :{" "}
                            <span className="text-muted f-s-14">
                              meeting invitation
                            </span>
                          </DropdownItem>
                        </DropdownMenu>
                      </Dropdown>
                    </div>
                  </div>
                  <div className="text-end d-none d-sm-block">
                    <p>Sep 29 2024, 4:00 PM</p>
                    <span className="badge text-light-primary">Company</span>
                  </div>
                  <div className="d-none d-sm-block">
                    <Dropdown
                      isOpen={dotsDropdownOpen}
                      toggle={() => setDotsDropdownOpen(!dotsDropdownOpen)}
                      direction="down"
                    >
                      <DropdownToggle
                        color="link"
                        className="icon-btn border-0"
                      >
                        <IconDotsVertical size={18} />
                      </DropdownToggle>
                      <DropdownMenu>
                        <DropdownItem>
                          <IconArchive size={18} className="me-2" />
                          Archive
                        </DropdownItem>
                        <DropdownItem>
                          <IconTrash size={18} className="me-2" />
                          Delete
                        </DropdownItem>
                        <DropdownItem>
                          <IconMailOpened size={18} className="me-2" />
                          Read Mail
                        </DropdownItem>
                      </DropdownMenu>
                    </Dropdown>
                  </div>
                </div>
                <div>
                  <div className="mb-3">
                    <h6>Hello! Bette</h6>
                  </div>
                  <div className="mb-3 text-secondary">
                    <p>
                      I hope you&#39;re doing well. I would like to schedule a
                      one-on-one meeting with you to{" "}
                      <strong>discussing a new project</strong>. I&#39;ll send
                      over the agenda in advance.
                    </p>
                    <p>
                      The meeting will be in my office, will you be available
                      one-on-one
                      <strong> 10 Oct,2024 at 10PM ?</strong> It&#39;s important
                      that we have this meeting so that we can continue to work
                      effectively together.
                    </p>
                  </div>
                  <div className="mb-3 text-secondary">
                    <p>I hope you can make it!</p>
                    <p>Best,</p>
                  </div>
                  <p className="f-w-500">AR team</p>
                </div>
                <div className="app-divider-v dotted"></div>
                <div className="mb-3">
                  <h6>
                    <IconPaperclip size={18} /> Attached
                  </h6>
                  <div className="data-list-box d-flex flex-wrap gap-2 mt-3">
                    {files.map((file, index) => (
                      <div className="filebox" key={index}>
                        <div className="d-flex gap-3 align-items-center position-relative">
                          <div className="position-absolute start-0">
                            <img
                              src={file.icon}
                              className="w-35 h-35"
                              alt="File Icon"
                            />
                          </div>

                          <div className="flex-grow-1 ms-5">
                            <h6 className="mb-0">{file.title}</h6>
                            <p className="text-secondary mb-0">
                              {file.details}
                            </p>
                          </div>

                          <p className="file-data text-secondary f-w-500 mb-0">
                            <IconDownload size={18} />
                          </p>
                        </div>
                      </div>
                    ))}
                  </div>
                </div>
                <div className="mb-3">
                  <TextEditor />
                </div>
                <button className="btn btn-primary mb-1" type="button">
                  <IconArrowBackUp /> Reply
                </button>{" "}
                <button className="btn btn-primary mb-1" type="button">
                  <IconArrowBackUpDouble /> Reply All
                </button>{" "}
                <button className="btn btn-primary mb-1" type="button">
                  <IconArrowForwardUp /> Forward
                </button>
              </div>
            </CardBody>
          </Card>
        </Col>
      </Row>
    </Container>
  );
};

export default ReadEmailPage;

"use client";
import React, { useState } from "react";
import Breadcrumbs from "@/Component/CommonElements/Breadcrumbs";
import {
  Container,
  Row,
  Button,
  Card,
  CardBody,
  Col,
  Form,
  FormGroup,
  Input,
  Label,
  Modal,
  ModalBody,
  ModalFooter,
  ModalHeader,
  Nav,
  NavItem,
  NavLink,
  InputGroup,
  InputGroupText,
  Dropdown,
  DropdownToggle,
  DropdownMenu,
  DropdownItem,
  TabContent,
  TabPane,
  Badge,
} from "reactstrap";
import {
  IconMail,
  IconSend,
  IconFile,
  IconStar,
  IconAlertOctagon,
  IconTrash,
  IconCircleFilled,
  IconAlignJustified,
  IconAlbum,
  IconTag,
  IconUsers,
  IconDotsVertical,
  IconSearch,
  IconArchive,
  IconMailOpened,
  IconStack2,
} from "@tabler/icons-react";
import {
  draftData,
  sentData,
  starredData,
  trashData,
} from "@/Data/Apps/Email/email";
import Link from "next/link";

const emailTabs = [
  { icon: <IconMail size={18} />, label: "Inbox", badge: "10+", tab: 1 },
  { icon: <IconSend size={18} />, label: "Sent", tab: 2 },
  { icon: <IconFile size={18} />, label: "Draft", tab: 3 },
  { icon: <IconStar size={18} />, label: "Starred", badge: "2+", tab: 4 },
  { icon: <IconAlertOctagon size={18} />, label: "Spam", tab: 5 },
  { icon: <IconTrash size={18} />, label: "Trash", tab: 6 },
];

const labels = [
  { color: "text-danger", label: "Social" },
  { color: "text-primary", label: "Company" },
  { color: "text-success", label: "Important" },
  { color: "text-info", label: "Private" },
];

const categories = [
  { icon: <IconMail size={18} />, label: "All Mail" },
  { icon: <IconAlbum size={18} />, label: "Primary" },
  { icon: <IconTag size={18} />, label: "Promotions" },
  { icon: <IconUsers size={18} />, label: "Social" },
];

const EmailPage = () => {
  const [isModalOpen, setIsModalOpen] = useState(false);
  const toggleModal = () => setIsModalOpen(!isModalOpen);
  const [activeTab, setActiveTab] = useState(1);
  const toggleTab = (tab: number) => {
    if (activeTab !== tab) {
      setActiveTab(tab);
    }
  };

  const [dropdownOpen, setDropdownOpen] = useState(false);
  const toggleDropdown = () => setDropdownOpen(!dropdownOpen);

  const [dropdowns, setDropdowns] = useState<Record<string, boolean>>({});
  const toggleDropdown1 = (id: string) => {
    setDropdowns((prev) => ({
      ...prev,
      [id]: !prev[id],
    }));
  };

  const [mailData, setMailData] = useState([
    {
      id: "inlineCheckbox1",
      name: "Gene Hart",
      imgSrc: "/images/avatar/14.png",
      date: "Sep 23",
      badgeColor: "light-success",
      badgeText: "Important",
      content:
        "This is the content of the email. It may contain anything the user....",
      isStarred: false,
    },
    {
      id: "inlineCheckbox21",
      name: "Neil Fisher",
      imgSrc: "/images/avatar/15.png",
      date: "Oct 23",
      badgeColor: "light-primary",
      badgeText: "Company",
      content:
        "It enables users to easily send and receive documents, images, links and ....",
      isStarred: true,
    },
    {
      id: "inlineCheckbox22",
      name: "Simon Young",
      imgSrc: "/images/avatar/13.png",
      date: "Dec 22",
      badgeColor: "light-danger",
      badgeText: "Social",
      content:
        "Companies can use email to convey information to a large number of ....",
      isStarred: false,
    },
  ]);

  interface NewMail {
    to: string;
    subject: string;
    status: string;
    message: string;
    file: File | null;
  }

  const [newMail, setNewMail] = useState<NewMail>({
    to: "",
    subject: "",
    status: "",
    message: "",
    file: null,
  });

  const handleInputChange = (
    e: React.ChangeEvent<
      HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement
    >
  ): void => {
    const { id, value } = e.target;
    setNewMail((prev) => ({
      ...prev,
      [id]: value,
    }));
  };

  const handleFormSubmit = () => {
    const updatedMailData = [
      ...mailData,
      {
        id: String(mailData.length + 1),
        name: newMail.to,
        content: newMail.message,
        date: new Date().toLocaleDateString(),
        badgeColor: "success",
        badgeText: newMail.status || "New",
        imgSrc: "/images/avatar/13.png",
        isStarred: false,
      },
    ];
    setMailData(updatedMailData);
    toggleModal();
  };
  return (
    <Container fluid>
      <Breadcrumbs
        mainTitle="Email"
        title="Apps"
        path={["Emails App", "Email"]}
        Icon={IconStack2}
      />
      <Row className="position-relative">
        <Col lg="3">
          <div className="mailbox">
            <Card>
              <CardBody>
                <div className="d-flex">
                  <Button
                    type="button"
                    color={"light-primary"}
                    className="w-100 rounded"
                    onClick={toggleModal}
                  >
                    Compose
                  </Button>
                  <div className="close-togglebtn">
                    <a className="ms-2 close-toggle" role="button">
                      <IconAlignJustified size={20} />
                    </a>
                  </div>
                </div>

                <div className="horizontal-tab-wrapper">
                  <Nav vertical pills className="email-list mt-3 tabs">
                    {emailTabs.map(({ icon, label, badge, tab }) => (
                      <NavItem key={label} className="w-100 d-block p-0">
                        <NavLink
                          onClick={() => toggleTab(tab)}
                          className={`cursor-pointer d-flex align-items-center tab-link ${activeTab === tab ? "active" : ""}`}
                        >
                          <span className="pe-2">{icon}</span>
                          <span className="flex-grow-1">{label}</span>
                          {badge && <span className="ms-1">{badge}</span>}
                        </NavLink>
                      </NavItem>
                    ))}
                  </Nav>
                </div>

                <div className="app-divider-v dashed p-2" />

                <ul className="email-list">
                  <li>
                    <h6>Labels</h6>
                  </li>
                  {labels.map(({ color, label }) => (
                    <li key={label} className={`f-w-500 ${color}`}>
                      <IconCircleFilled className={`pe-2 ${color}`} size={18} />
                      {label}
                    </li>
                  ))}
                </ul>

                <div className="app-divider-v dashed p-2" />

                <ul className="email-list">
                  {categories.map(({ icon, label }) => (
                    <li key={label} className="f-w-500">
                      <span className="pe-2">{icon}</span> {label}
                    </li>
                  ))}
                </ul>
              </CardBody>
            </Card>
          </div>

          {/* Modal */}
          <Modal
            isOpen={isModalOpen}
            toggle={toggleModal}
            backdrop="static"
            keyboard={false}
          >
            <ModalHeader toggle={toggleModal}>New Message</ModalHeader>
            <ModalBody>
              <Form className="app-form">
                <FormGroup>
                  <Label for="to">To :</Label>
                  <Input
                    type="email"
                    id="to"
                    placeholder="@gmail.com"
                    value={newMail.to}
                    onChange={handleInputChange}
                  />
                </FormGroup>
                <FormGroup>
                  <Label for="subject">Subject :</Label>
                  <Input
                    type="text"
                    id="subject"
                    placeholder="type subject..."
                    value={newMail.subject}
                    onChange={handleInputChange}
                  />
                </FormGroup>
                <FormGroup>
                  <Label for="status">Status :</Label>
                  <Input
                    type="select"
                    id="status"
                    value={newMail.status}
                    onChange={handleInputChange}
                  >
                    <option value="">Status</option>
                    <option>Important</option>
                    <option>Company</option>
                    <option>Social</option>
                    <option>Private</option>
                  </Input>
                </FormGroup>
                <FormGroup>
                  <Label for="message">Message</Label>
                  <Input
                    type="textarea"
                    id="message"
                    value={newMail.message}
                    onChange={handleInputChange}
                  />
                </FormGroup>
                <FormGroup>
                  <Label>Attached File</Label>
                  <Input
                    type="file"
                    id="file"
                    onChange={(e) => {
                      const file = e.target.files?.[0];
                      if (file) {
                        setNewMail((prev) => ({
                          ...prev,
                          file: file,
                        }));
                      }
                    }}
                  />
                </FormGroup>
              </Form>
            </ModalBody>
            <ModalFooter>
              <Button color="secondary" onClick={toggleModal}>
                Close
              </Button>
              <Button color="primary" onClick={handleFormSubmit}>
                Send
              </Button>
            </ModalFooter>
          </Modal>
        </Col>
        <Col lg="9">
          <Card>
            <CardBody>
              <div className="d-flex align-items-center">
                <div className="d-lg-none me-3">
                  <a className="toggle-btn" role="button">
                    <IconAlignJustified />
                  </a>
                </div>
                <div className="flex-grow-1">
                  <InputGroup className="b-r-search">
                    <InputGroupText className="bg-primary border-0">
                      <IconSearch size={16} />
                    </InputGroupText>
                    <Input placeholder="Search..." type="text" />
                  </InputGroup>
                </div>
                <div className="ms-3">
                  <Dropdown
                    isOpen={dropdownOpen}
                    toggle={toggleDropdown}
                    className="dropdown-icon-none"
                  >
                    <DropdownToggle
                      color="light-primary"
                      className="icon-btn p-2"
                      caret={false}
                    >
                      <IconDotsVertical />
                    </DropdownToggle>
                    <DropdownMenu end>
                      <DropdownItem href="#">
                        <IconAlbum className="me-2" size={18} />
                        Primary
                      </DropdownItem>
                      <DropdownItem href="#">
                        <IconTag className="me-2" size={18} />
                        Promotions
                      </DropdownItem>
                      <DropdownItem href="#">
                        <IconUsers className="me-2" size={18} />
                        Social
                      </DropdownItem>
                    </DropdownMenu>
                  </Dropdown>
                </div>
              </div>
              <div className="content-wrapper mt-3">
                <TabContent activeTab={activeTab.toString()}>
                  <TabPane tabId="1">
                    <div className="mail-table">
                      {mailData.map((mail) => (
                        <div
                          className="mail-box d-flex align-items-center p-3"
                          key={mail.id}
                        >
                          <Input
                            className="form-check-input"
                            id={mail.id}
                            type="checkbox"
                          />
                          <span className="ms-2 me-2">
                            <i
                              className={`ti ${mail.isStarred ? "ti-star-filled" : "ti-star"} text-warning star-icon fs-5`}
                            />
                          </span>
                          <div className="flex-grow-1 position-relative">
                            <div className="mail-img h-35 w-35 b-r-50 overflow-hidden text-bg-primary position-absolute mt-1">
                              <img
                                alt=""
                                className="img-fluid"
                                src={mail.imgSrc}
                              />
                            </div>
                            <div className="mg-s-45">
                              <h6 className="mb-0 f-w-600">{mail.name}</h6>
                              <Link href="/apps/email-page/read-email">
                                <span className="f-s-13 text-secondary">
                                  {mail.content}
                                </span>
                              </Link>
                            </div>
                          </div>
                          <div>
                            <p className="text-center">{mail.date}</p>
                            <Badge color={mail.badgeColor}>
                              {mail.badgeText}
                            </Badge>
                          </div>
                          <div>
                            <Dropdown
                              isOpen={dropdowns[mail.id] || false}
                              toggle={() => toggleDropdown1(mail.id)}
                            >
                              <DropdownToggle
                                color="light-primary"
                                className="w-25 h-25 p-1 border-0 icon-btn b-r-4"
                                caret={false}
                              >
                                <IconDotsVertical />
                              </DropdownToggle>
                              <DropdownMenu>
                                <DropdownItem>
                                  <IconArchive size={16} /> Archive
                                </DropdownItem>
                                <DropdownItem>
                                  <IconTrash size={16} /> Delete
                                </DropdownItem>
                                <DropdownItem>
                                  <IconMailOpened size={16} /> Read Mail
                                </DropdownItem>
                              </DropdownMenu>
                            </Dropdown>
                          </div>
                        </div>
                      ))}
                    </div>
                  </TabPane>
                  <TabPane tabId="2">
                    <div className="mail-table">
                      {sentData.map((mail) => (
                        <div
                          className="mail-box d-flex align-items-center p-3"
                          key={mail.id}
                        >
                          <Input
                            className="form-check-input"
                            id={mail.id}
                            type="checkbox"
                          />
                          <span className="ms-2 me-2">
                            <i
                              className={`ti ${mail.isStarred ? "ti-star-filled" : "ti-star"} text-warning star-icon fs-5`}
                            />
                          </span>
                          <div className="flex-grow-1 position-relative">
                            <div className="mail-img h-35 w-35 b-r-50 overflow-hidden text-bg-primary position-absolute mt-1">
                              <img
                                alt=""
                                className="img-fluid"
                                src={mail.imgSrc}
                              />
                            </div>
                            <div className="mg-s-45">
                              <h6 className="mb-0 f-w-600">{mail.name}</h6>
                              <Link href="/apps/email-page/read-email">
                                <span className="f-s-13 text-secondary">
                                  {mail.content}
                                </span>
                              </Link>
                            </div>
                          </div>
                          <div>
                            <p className="text-center">{mail.date}</p>
                            <Badge color={mail.badgeColor}>
                              {mail.badgeText}
                            </Badge>
                          </div>
                          <div>
                            <Dropdown
                              isOpen={dropdowns[mail.id] || false}
                              toggle={() => toggleDropdown1(mail.id)}
                            >
                              <DropdownToggle
                                color="light-primary"
                                className="w-25 h-25 p-1 border-0 icon-btn b-r-4"
                                caret={false}
                              >
                                <IconDotsVertical />
                              </DropdownToggle>
                              <DropdownMenu>
                                <DropdownItem>
                                  <IconArchive size={16} /> Archive
                                </DropdownItem>
                                <DropdownItem>
                                  <IconTrash size={16} /> Delete
                                </DropdownItem>
                                <DropdownItem>
                                  <IconMailOpened size={16} /> Read Mail
                                </DropdownItem>
                              </DropdownMenu>
                            </Dropdown>
                          </div>
                        </div>
                      ))}
                    </div>
                  </TabPane>
                  <TabPane tabId="3">
                    <div className="mail-table">
                      {draftData.map((mail) => (
                        <div
                          className="mail-box d-flex align-items-center p-3"
                          key={mail.id}
                        >
                          <Input
                            className="form-check-input"
                            id={mail.id}
                            type="checkbox"
                          />
                          <span className="ms-2 me-2">
                            <i
                              className={`ti ${mail.isStarred ? "ti-star-filled" : "ti-star"} text-warning star-icon fs-5`}
                            />
                          </span>
                          <div className="flex-grow-1 position-relative">
                            <div className="mail-img h-35 w-35 b-r-50 overflow-hidden text-bg-primary position-absolute mt-1">
                              <img
                                alt=""
                                className="img-fluid"
                                src={mail.imgSrc}
                              />
                            </div>
                            <div className="mg-s-45">
                              <h6 className="mb-0 f-w-600">{mail.name}</h6>
                              <Link href="/apps/email-page/read-email">
                                <span className="f-s-13 text-secondary">
                                  {mail.content}
                                </span>
                              </Link>
                            </div>
                          </div>
                          <div>
                            <p className="text-center">{mail.date}</p>
                            <Badge color={mail.badgeColor}>
                              {mail.badgeText}
                            </Badge>
                          </div>
                          <div>
                            <Dropdown
                              isOpen={dropdowns[mail.id] || false}
                              toggle={() => toggleDropdown1(mail.id)}
                            >
                              <DropdownToggle
                                color="light-primary"
                                className="w-25 h-25 p-1 border-0 icon-btn b-r-4"
                                caret={false}
                              >
                                <IconDotsVertical />
                              </DropdownToggle>
                              <DropdownMenu>
                                <DropdownItem>
                                  <IconArchive size={16} /> Archive
                                </DropdownItem>
                                <DropdownItem>
                                  <IconTrash size={16} /> Delete
                                </DropdownItem>
                                <DropdownItem>
                                  <IconMailOpened size={16} /> Read Mail
                                </DropdownItem>
                              </DropdownMenu>
                            </Dropdown>
                          </div>
                        </div>
                      ))}
                    </div>
                  </TabPane>
                  <TabPane tabId="4">
                    <div className="mail-table">
                      {starredData.map((mail) => (
                        <div
                          className="mail-box d-flex align-items-center p-3"
                          key={mail.id}
                        >
                          <Input
                            className="form-check-input"
                            id={mail.id}
                            type="checkbox"
                          />
                          <span className="ms-2 me-2">
                            <i
                              className={`ti ${mail.isStarred ? "ti-star-filled" : "ti-star"} text-warning star-icon fs-5`}
                            />
                          </span>
                          <div className="flex-grow-1 position-relative">
                            <div className="mail-img h-35 w-35 b-r-50 overflow-hidden text-bg-primary position-absolute mt-1">
                              <img
                                alt=""
                                className="img-fluid"
                                src={mail.imgSrc}
                              />
                            </div>
                            <div className="mg-s-45">
                              <h6 className="mb-0 f-w-600">{mail.name}</h6>
                              <Link href="/apps/email-page/read-email">
                                <span className="f-s-13 text-secondary">
                                  {mail.content}
                                </span>
                              </Link>
                            </div>
                          </div>
                          <div>
                            <p className="text-center">{mail.date}</p>
                            <Badge color={mail.badgeColor}>
                              {mail.badgeText}
                            </Badge>
                          </div>
                          <div>
                            <Dropdown
                              isOpen={dropdowns[mail.id] || false}
                              toggle={() => toggleDropdown1(mail.id)}
                            >
                              <DropdownToggle
                                color="light-primary"
                                className="w-25 h-25 p-1 border-0 icon-btn b-r-4"
                                caret={false}
                              >
                                <IconDotsVertical />
                              </DropdownToggle>
                              <DropdownMenu>
                                <DropdownItem>
                                  <IconArchive size={16} /> Archive
                                </DropdownItem>
                                <DropdownItem>
                                  <IconTrash size={16} /> Delete
                                </DropdownItem>
                                <DropdownItem>
                                  <IconMailOpened size={16} /> Read Mail
                                </DropdownItem>
                              </DropdownMenu>
                            </Dropdown>
                          </div>
                        </div>
                      ))}
                    </div>
                  </TabPane>
                  <TabPane tabId="5">
                    <div className="mail-table d-flex align-items-center justify-content-center">
                      <div className="spam-box text-center">
                        <img alt="" src="/images/icons/spam..png" />
                        <h5>No spam here</h5>
                        <p>
                          The MUA formats the message in email format and uses
                          the submission a profile of the Simple Mail Transfer
                          Protocol (SMTP), to send the message !
                        </p>
                      </div>
                    </div>
                  </TabPane>
                  <TabPane tabId="6">
                    <div className="mail-table">
                      {trashData.map((mail) => (
                        <div
                          className="mail-box d-flex align-items-center p-3"
                          key={mail.id}
                        >
                          <Input
                            className="form-check-input"
                            id={mail.id}
                            type="checkbox"
                          />
                          <span className="ms-2 me-2">
                            <i
                              className={`ti ${mail.isStarred ? "ti-star-filled" : "ti-star"} text-warning star-icon fs-5`}
                            />
                          </span>
                          <div className="flex-grow-1 position-relative">
                            <div className="mail-img h-35 w-35 b-r-50 overflow-hidden text-bg-primary position-absolute mt-1">
                              <img
                                alt=""
                                className="img-fluid"
                                src={mail.imgSrc}
                              />
                            </div>
                            <div className="mg-s-45">
                              <h6 className="mb-0 f-w-600">{mail.name}</h6>
                              <Link href="/apps/email-page/read-email">
                                <span className="f-s-13 text-secondary">
                                  {mail.content}
                                </span>
                              </Link>
                            </div>
                          </div>
                          <div>
                            <p className="text-center">{mail.date}</p>
                            <Badge color={mail.badgeColor}>
                              {mail.badgeText}
                            </Badge>
                          </div>
                          <div>
                            <Dropdown
                              isOpen={dropdowns[mail.id] || false}
                              toggle={() => toggleDropdown1(mail.id)}
                            >
                              <DropdownToggle
                                color="light-primary"
                                className="w-25 h-25 p-1 border-0 icon-btn b-r-4"
                                caret={false}
                              >
                                <IconDotsVertical />
                              </DropdownToggle>
                              <DropdownMenu>
                                <DropdownItem>
                                  <IconArchive size={16} /> Archive
                                </DropdownItem>
                                <DropdownItem>
                                  <IconTrash size={16} /> Delete
                                </DropdownItem>
                                <DropdownItem>
                                  <IconMailOpened size={16} /> Read Mail
                                </DropdownItem>
                              </DropdownMenu>
                            </Dropdown>
                          </div>
                        </div>
                      ))}
                    </div>
                  </TabPane>
                </TabContent>
              </div>
            </CardBody>
          </Card>
        </Col>
      </Row>
    </Container>
  );
};

export default EmailPage;

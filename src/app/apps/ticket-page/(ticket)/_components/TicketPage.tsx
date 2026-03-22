"use client";
import React, { useState } from "react";
import {
  Badge,
  Button,
  Card,
  CardBody,
  Col,
  Container,
  Form,
  FormGroup,
  Input,
  Label,
  Modal,
  ModalBody,
  ModalFooter,
  ModalHeader,
  Row,
} from "reactstrap";
import Breadcrumbs from "@/Component/CommonElements/Breadcrumbs";
import { ticketsAppDatas } from "@/Data/Apps/Ticket/Ticket";
import { Circle } from "phosphor-react";
import Slider from "react-slick";
import { IconStack2 } from "@tabler/icons-react";
import CustomDataTable from "@/Component/DataTable/CustomDataTable";

interface Category {
  name: string;
  count: number;
}

const categories: Category[] = [
  { name: "Laptop issues", count: 64 },
  { name: "Card issues", count: 52 },
  { name: "Admin issues", count: 32 },
  { name: "Laptop issues", count: 20 },
];

const settings = {
  slidesToShow: 2,
  slidesToScroll: 1,
  autoplay: true,
  autoplaySpeed: 2000,
  responsive: [
    { breakpoint: 768, settings: { slidesToShow: 3 } },
    { breakpoint: 576, settings: { slidesToShow: 1 } },
  ],
};

type Ticket = {
  id: string;
  agent: string;
  agentAvatar: string;
  agentAvatarBg: string;
  priority: string;
  priorityColor: string;
  title: string;
  status: string;
  statusColor: string;
  date: string;
  dueDate: string;
};

const initialTickets: Ticket[] = [
  {
    id: "AR 2044",
    agent: "Gavin Cortez",
    agentAvatar: "/images/avatar/1.png",
    agentAvatarBg: "primary",
    priority: "Medium",
    priorityColor: "warning",
    title: "Bug Report",
    status: "in progress",
    statusColor: "success",
    date: "1 Jan 2024",
    dueDate: "3 Feb 2024",
  },
  {
    id: "AR 1763",
    agent: "Martena Mccray",
    agentAvatar: "/images/avatar/14.png",
    agentAvatarBg: "dark",
    priority: "Low",
    priorityColor: "danger",
    title: "Feature Request",
    status: "closed",
    statusColor: "info",
    date: "8 Jan 2024",
    dueDate: "10 Mar 2024",
  },
  {
    id: "AR 1987",
    agent: "Daryl Hawker",
    agentAvatar: "/images/avatar/5.png",
    agentAvatarBg: "success",
    priority: "High",
    priorityColor: "danger",
    title: "Performance Issue",
    status: "open",
    statusColor: "primary",
    date: "12 Jan 2024",
    dueDate: "20 Feb 2024",
  },
  {
    id: "AR 1899",
    agent: "Rebecca Moore",
    agentAvatar: "/images/avatar/7.png",
    agentAvatarBg: "warning",
    priority: "Medium",
    priorityColor: "warning",
    title: "UI Improvement",
    status: "in progress",
    statusColor: "success",
    date: "15 Jan 2024",
    dueDate: "28 Feb 2024",
  },
  {
    id: "AR 1555",
    agent: "Thomas Lee",
    agentAvatar: "/images/avatar/2.png",
    agentAvatarBg: "info",
    priority: "Low",
    priorityColor: "info",
    title: "General Inquiry",
    status: "pending",
    statusColor: "warning",
    date: "18 Jan 2024",
    dueDate: "1 Mar 2024",
  },
  {
    id: "AR 1321",
    agent: "Alicia Keys",
    agentAvatar: "/images/avatar/12.png",
    agentAvatarBg: "secondary",
    priority: "High",
    priorityColor: "danger",
    title: "Security Vulnerability",
    status: "open",
    statusColor: "danger",
    date: "20 Jan 2024",
    dueDate: "25 Feb 2024",
  },
  {
    id: "AR 1678",
    agent: "Samuel Thompson",
    agentAvatar: "/images/avatar/8.png",
    agentAvatarBg: "primary",
    priority: "Medium",
    priorityColor: "warning",
    title: "Integration Bug",
    status: "in review",
    statusColor: "info",
    date: "22 Jan 2024",
    dueDate: "5 Mar 2024",
  },
  {
    id: "AR 1730",
    agent: "Natalie Portman",
    agentAvatar: "/images/avatar/11.png",
    agentAvatarBg: "danger",
    priority: "Low",
    priorityColor: "secondary",
    title: "Feedback Follow-up",
    status: "closed",
    statusColor: "secondary",
    date: "24 Jan 2024",
    dueDate: "15 Mar 2024",
  },
  {
    id: "AR 1920",
    agent: "Kevin Malone",
    agentAvatar: "/images/avatar/9.png",
    agentAvatarBg: "light",
    priority: "High",
    priorityColor: "primary",
    title: "Login Issue",
    status: "open",
    statusColor: "primary",
    date: "26 Jan 2024",
    dueDate: "12 Mar 2024",
  },
  {
    id: "AR 1650",
    agent: "Pam Beesly",
    agentAvatar: "/images/avatar/13.png",
    agentAvatarBg: "success",
    priority: "Medium",
    priorityColor: "warning",
    title: "Content Update",
    status: "pending",
    statusColor: "warning",
    date: "28 Jan 2024",
    dueDate: "18 Mar 2024",
  },
];

const TicketPage = () => {
  const [selectedTicketId, setSelectedTicketId] = useState<string | null>(null);
  const [deleteModal, setDeleteModal] = useState(false);
  const [ticketList, setTicketList] = useState<Ticket[]>(initialTickets);
  const [modal, setModal] = useState(false);
  const [nextId, setNextId] = useState(1);
  const [selectedRows, setSelectedRows] = useState<string[]>([]);

  const toggleRowSelection = (id: string) => {
    setSelectedRows((prev) =>
      prev.includes(id) ? prev.filter((rowId) => rowId !== id) : [...prev, id]
    );
  };

  const toggleSelectAll = () => {
    if (selectedRows.length === ticketList.length) {
      setSelectedRows([]);
    } else {
      setSelectedRows(ticketList.map((ticket) => ticket.id));
    }
  };

  const [formData, setFormData] = useState({
    title: "",
    client: "",
    priority: "",
    status: "",
    date: "",
    dueDate: "",
  });
  const toggleModal = () => setModal(!modal);

  const getPriorityColor = (priority: string) => {
    switch (priority.toLowerCase()) {
      case "high":
        return "danger";
      case "medium":
        return "warning";
      case "lower":
        return "success";
      default:
        return "secondary";
    }
  };

  const getStatusColor = (status: string) => {
    switch (status.toLowerCase()) {
      case "open":
        return "info";
      case "in progress":
        return "warning";
      case "closed":
        return "success";
      default:
        return "secondary";
    }
  };

  const handleSubmit = () => {
    const newTicket: Ticket = {
      id: `AR ${nextId}`,
      title: formData.title,
      agent: "Tom Hardy",
      agentAvatar: "/images/avatar/13.png",
      agentAvatarBg: "primary",
      priority: formData.priority,
      priorityColor: getPriorityColor(formData.priority),
      status: formData.status,
      statusColor: getStatusColor(formData.status),
      date: formData.date,
      dueDate: formData.dueDate,
    };

    setTicketList([...ticketList, newTicket]);
    setNextId(nextId + 1);
    setFormData({
      title: "",
      client: "",
      priority: "",
      status: "",
      date: "",
      dueDate: "",
    });
    toggleModal();
  };

  const handleDelete = () => {
    if (selectedTicketId) {
      setTicketList((prev) => {
        const updatedList = prev.filter(
          (ticket) => ticket.id !== selectedTicketId
        );
        return updatedList;
      });
      setSelectedTicketId(null);
      setDeleteModal(false);
    }
  };

  const columns = [
    {
      key: "checkbox",
      header: (
        <input
          type="checkbox"
          checked={
            selectedRows.length === ticketList.length && ticketList.length > 0
          }
          onChange={toggleSelectAll}
        />
      ),
      render: (_: any, item: Ticket) => (
        <input
          type="checkbox"
          checked={selectedRows.includes(item.id)}
          onChange={() => toggleRowSelection(item.id)}
        />
      ),
    },
    {
      key: "id",
      header: "Id",
      render: (value: string) => <span className="fw-bold">{value}</span>,
    },
    {
      key: "agent",
      header: "Agent",
      render: (_: any, item: Ticket) => (
        <div className="d-flex align-items-center">
          <div
            className={`h-30 w-30 d-flex-center b-r-50 overflow-hidden text-bg-${item.agentAvatarBg} me-2`}
          >
            <img src={item.agentAvatar} alt="avatar" className="img-fluid" />
          </div>
          {item.agent}
        </div>
      ),
    },
    {
      key: "priority",
      header: "Priority",
      render: (value: string, item: Ticket) => (
        <Badge color={`outline-${item.priorityColor}`}>{value}</Badge>
      ),
    },
    { key: "title", header: "Title" },
    {
      key: "status",
      header: "Status",
      render: (value: string, item: Ticket) => (
        <Badge color={`outline-${item.statusColor}`}>{value}</Badge>
      ),
    },
    { key: "date", header: "Date" },
    { key: "dueDate", header: "Due Date" },
  ];

  return (
    <Container fluid>
      <Breadcrumbs
        mainTitle="Ticket"
        title="Apps"
        path={["Ticket"]}
        Icon={IconStack2}
      />

      <Row className="ticket-app">
        <Col lg={6}>
          <Row>
            {ticketsAppDatas.map((ticket, index) => (
              <Col sm="6" key={index}>
                <Card className={`ticket-card bg-${ticket.bgColor}`}>
                  <CardBody>
                    <Circle
                      width={118}
                      height={118}
                      weight="bold"
                      className="circle-bg-img"
                    />
                    <div className="h-50 w-50 d-flex-center b-r-15 bg-white mb-3">
                      <ticket.icon
                        weight="bold"
                        className={`f-s-25 text-${ticket.textColor}`}
                      />
                    </div>
                    <p className="f-s-16">{ticket.title}</p>
                    <div className="d-flex justify-content-between align-items-center">
                      <h3 className="text-dark">{ticket.count}</h3>
                      <ul className="avatar-group">
                        {ticket.avatars.map((avatar, idx) => (
                          <li
                            key={idx}
                            className={`h-45 w-45 d-flex-center b-r-50 text-bg-${avatar.bgColor} b-2-light position-relative`}
                          >
                            <span
                              className={`position-absolute top-0 start-2 p-1 bg-${avatar.bgColor} border border-light rounded-circle`}
                            ></span>
                            <img
                              src={avatar.img}
                              alt={avatar.tooltip}
                              className="img-fluid b-r-50 overflow-hidden"
                            />
                          </li>
                        ))}
                        <li className="bg-white text-dark h-35 w-35 d-flex-center b-r-50">
                          {`${ticket.extraCount}+`}
                        </li>
                      </ul>
                    </div>
                  </CardBody>
                </Card>
              </Col>
            ))}
          </Row>
        </Col>

        <Col lg={6}>
          <Card className="create-ticket-card">
            <CardBody>
              <Row className="align-items-center">
                <Col sm="7">
                  <h5 className="mb-2">The Ticket Component</h5>
                  <p className="mb-5 mt-3 text-secondary">
                    Provide a more detailed explanation of the issue or desired
                    feature.
                  </p>
                  <Button
                    color="primary"
                    className="mb-3"
                    onClick={toggleModal}
                  >
                    Create Ticket
                  </Button>
                </Col>
                <Col sm="5">
                  <img
                    src="/images/icons/ticket.png"
                    alt="Ticket Icon"
                    className="img-fluid d-block m-auto max-w-300"
                  />
                </Col>
              </Row>
            </CardBody>
          </Card>

          <h5 className="ms-3 mb-2">Top Category</h5>
          <Slider {...settings} className="ticket-slider">
            {categories.map((category, index) => (
              <li key={index}>
                <div className="ticket-catagory p-3">
                  <h6 className="mb-0">{category.name}</h6>
                  <span className="badge text-light-success">
                    {category.count}
                  </span>
                </div>
              </li>
            ))}
          </Slider>
        </Col>

        <Col sm={12}>
          <CustomDataTable
            key={ticketList.length}
            title=""
            description=""
            columns={columns}
            data={ticketList}
            onEdit={(item) => console.log("Edit", item)}
            onDelete={(item) => {
              if (item?.id) {
                setSelectedTicketId(item.id);
                setDeleteModal(true);
              }
            }}
          />
        </Col>
      </Row>

      <Modal isOpen={modal} toggle={toggleModal} centered>
        <ModalHeader toggle={toggleModal}>Add Ticket</ModalHeader>
        <ModalBody>
          <Form className="app-form">
            <FormGroup>
              <Label>Title</Label>
              <Input
                value={formData.title}
                onChange={(e) =>
                  setFormData({ ...formData, title: e.target.value })
                }
              />
            </FormGroup>
            <FormGroup>
              <Label>Client</Label>
              <Input
                value={formData.client}
                onChange={(e) =>
                  setFormData({ ...formData, client: e.target.value })
                }
              />
            </FormGroup>
            <Row>
              <Col md={6}>
                <FormGroup>
                  <Label>Priority</Label>
                  <Input
                    type="select"
                    value={formData.priority}
                    onChange={(e) =>
                      setFormData({ ...formData, priority: e.target.value })
                    }
                  >
                    <option value="">Select Priority</option>
                    <option value="High">High</option>
                    <option value="Medium">Medium</option>
                    <option value="Lower">Lower</option>
                  </Input>
                </FormGroup>
              </Col>
              <Col md={6}>
                <FormGroup>
                  <Label>Status</Label>
                  <Input
                    type="select"
                    value={formData.status}
                    onChange={(e) =>
                      setFormData({ ...formData, status: e.target.value })
                    }
                  >
                    <option value="">Select Status</option>
                    <option value="open">Open</option>
                    <option value="in progress">In Progress</option>
                    <option value="closed">Closed</option>
                  </Input>
                </FormGroup>
              </Col>
              <Col md={6}>
                <FormGroup>
                  <Label>Date</Label>
                  <Input
                    type="date"
                    value={formData.date}
                    onChange={(e) =>
                      setFormData({ ...formData, date: e.target.value })
                    }
                  />
                </FormGroup>
              </Col>
              <Col md={6}>
                <FormGroup>
                  <Label>Due Date</Label>
                  <Input
                    type="date"
                    value={formData.dueDate}
                    onChange={(e) =>
                      setFormData({ ...formData, dueDate: e.target.value })
                    }
                  />
                </FormGroup>
              </Col>
            </Row>
          </Form>
        </ModalBody>
        <ModalFooter>
          <Button color="secondary" onClick={toggleModal}>
            Close
          </Button>
          <Button color="primary" onClick={handleSubmit}>
            Save changes
          </Button>
        </ModalFooter>
      </Modal>
      <Modal isOpen={deleteModal} toggle={() => setDeleteModal(false)} centered>
        <ModalBody className="text-center">
          <img
            alt="Delete Icon"
            className="img-fluid mb-3"
            src="/images/icons/delete-icon.png"
          />
          <h4 className="text-danger fw-bold">Are You Sure?</h4>
          <p className="text-secondary fs-6">
            You won&#39;t be able to revert this!
          </p>
          <div className="mt-3 d-flex justify-content-center gap-2">
            <Button color="secondary" onClick={() => setDeleteModal(false)}>
              Close
            </Button>
            <Button color="primary" onClick={handleDelete}>
              Yes, Delete it
            </Button>
          </div>
        </ModalBody>
      </Modal>
    </Container>
  );
};

export default TicketPage;

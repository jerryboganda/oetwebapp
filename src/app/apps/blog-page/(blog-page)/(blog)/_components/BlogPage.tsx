"use client";
import {
  Card,
  CardBody,
  Row,
  Modal,
  ModalHeader,
  ModalBody,
  ModalFooter,
  Button,
  Form,
  FormGroup,
  Label,
  Input,
  Container,
  Dropdown,
  DropdownToggle,
  DropdownMenu,
  DropdownItem,
} from "reactstrap";
import React, { useEffect, useState, useCallback } from "react";
import Link from "next/link";
import Breadcrumbs from "@/Component/CommonElements/Breadcrumbs";
import {
  IconArchive,
  IconCalendarDue,
  IconDotsVertical,
  IconStack2,
  IconTrash,
} from "@tabler/icons-react";
import { initialBlogData } from "@/Data/Apps/Blog/BlogData";

// Define types for blog data
interface Author {
  name: string;
  avatar: string;
  time: string;
}

interface Blog {
  id: number;
  colclass: string;
  image: string;
  tag: string;
  date: string;
  title: string;
  description: string;
  author: Author;
}

const BlogPage: React.FC = () => {
  const [blogs, setBlogs] = useState<Blog[]>([]);
  const [modalOpen, setModalOpen] = useState<boolean>(false);
  const [selectedBlog, setSelectedBlog] = useState<Blog | null>(null);
  const [dropdownOpen, setDropdownOpen] = useState<{ [key: number]: boolean }>(
    {}
  );
  const [lightboxOpen, setLightboxOpen] = useState<boolean>(false);
  const [selectedImage, setSelectedImage] = useState<string>("");

  useEffect(() => {
    setBlogs(initialBlogData);
  }, []);

  const toggleModal = () => setModalOpen((prev) => !prev);

  const toggleDropdown = (id: number) => {
    setDropdownOpen((prev) => ({
      ...prev,
      [id]: !prev[id],
    }));
  };

  const handleEditClick = (blog: Blog) => {
    setSelectedBlog(blog);
    toggleModal();
  };

  const handleDeleteClick = (id: number) => {
    setBlogs((prev) => prev.filter((blog) => blog.id !== id));
  };

  const handleImageClick = useCallback((imageUrl: string) => {
    setSelectedImage(imageUrl);
    setLightboxOpen(true);
  }, []);

  const closeLightbox = () => {
    setLightboxOpen(false);
    setSelectedImage("");
  };

  return (
    <div>
      <Container fluid>
        <Breadcrumbs
          mainTitle="Blog"
          title="Apps"
          path={["Blog-Page", "Blog"]}
          Icon={IconStack2}
        />
        <Row>
          {blogs.map((blog) => (
            <div className={blog.colclass} key={blog.id}>
              <Card className="blog-card overflow-hidden">
                <button
                  onClick={() => handleImageClick(blog.image)}
                  className="img-hover-zoom border-0 bg-transparent p-0 w-100"
                >
                  <img
                    src={blog.image}
                    className="card-img-top"
                    alt={blog.title}
                  />
                </button>
                <div className="tag-container">
                  <span className="badge text-light-secondary">{blog.tag}</span>
                </div>
                <CardBody>
                  <p className="text-body-secondary">
                    <IconCalendarDue size={18} />
                    {blog.date}
                  </p>
                  <Link href="/apps/blog-page/blog-details">
                    <h5 className="title-text mb-2">{blog.title}</h5>
                  </Link>
                  <p className="card-text text-secondary">{blog.description}</p>
                  <div className="app-divider-v dashed py-3"></div>
                  <div className="d-flex justify-content-between align-items-center gap-2 position-relative">
                    <div className="h-40 w-40 d-flex-center b-r-10 overflow-hidden bg-primary position-absolute">
                      <img
                        src={blog.author.avatar}
                        alt="avatar"
                        className="img-fluid"
                      />
                    </div>
                    <div className="ps-5">
                      <h6 className="text-dark f-w-500 mb-0">
                        {blog.author.name}
                      </h6>
                      <p className="text-secondary f-s-12 mb-0">
                        {blog.author.time}
                      </p>
                    </div>
                    <div>
                      <Dropdown
                        isOpen={dropdownOpen[blog.id] || false}
                        toggle={() => toggleDropdown(blog.id)}
                      >
                        <DropdownToggle className="border-0 icon-btn b-r-4 bg-transparent">
                          <IconDotsVertical size={18} className="text-dark" />
                        </DropdownToggle>
                        <DropdownMenu>
                          <DropdownItem
                            onClick={() => handleEditClick(blog)}
                            className="text-success"
                          >
                            <IconArchive size={16} className="me-2" />
                            Edit
                          </DropdownItem>
                          <DropdownItem
                            onClick={() => handleDeleteClick(blog.id)}
                            className="text-danger"
                          >
                            <IconTrash size={16} className="me-2" />
                            Delete
                          </DropdownItem>
                        </DropdownMenu>
                      </Dropdown>
                    </div>
                  </div>
                </CardBody>
              </Card>
            </div>
          ))}
        </Row>

        {/* Modal for Editing Blog */}
        {selectedBlog && (
          <Modal isOpen={modalOpen} toggle={toggleModal}>
            <ModalHeader toggle={toggleModal}>Edit Blog</ModalHeader>
            <ModalBody>
              <Form>
                <FormGroup>
                  <Label for="blogTitle">Title</Label>
                  <Input
                    type="text"
                    id="blogTitle"
                    value={selectedBlog.title}
                    onChange={(e) =>
                      setSelectedBlog({
                        ...selectedBlog,
                        title: e.target.value,
                      })
                    }
                  />
                </FormGroup>
                <FormGroup>
                  <Label for="blogDescription">Description</Label>
                  <Input
                    type="textarea"
                    id="blogDescription"
                    value={selectedBlog.description}
                    onChange={(e) =>
                      setSelectedBlog({
                        ...selectedBlog,
                        description: e.target.value,
                      })
                    }
                  />
                </FormGroup>
              </Form>
            </ModalBody>
            <ModalFooter>
              <Button color="secondary" onClick={toggleModal}>
                Close
              </Button>
              <Button
                color="primary"
                onClick={() => {
                  setBlogs((prevBlogs) =>
                    prevBlogs.map((blog) =>
                      blog.id === selectedBlog.id ? selectedBlog : blog
                    )
                  );
                  toggleModal();
                }}
              >
                Save Changes
              </Button>
            </ModalFooter>
          </Modal>
        )}

        {/* Lightbox Modal */}
        <Modal isOpen={lightboxOpen} toggle={closeLightbox} size="lg">
          <ModalHeader toggle={closeLightbox}>Image Preview</ModalHeader>
          <ModalBody className="text-center">
            <img
              src={selectedImage}
              alt="Preview"
              className="img-fluid"
              style={{ maxHeight: "70vh", objectFit: "contain" }}
            />
          </ModalBody>
          <ModalFooter>
            <Button color="secondary" onClick={closeLightbox}>
              Close
            </Button>
          </ModalFooter>
        </Modal>
      </Container>
    </div>
  );
};

export default BlogPage;
